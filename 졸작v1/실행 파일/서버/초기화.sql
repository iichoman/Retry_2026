/* ==========================================================================
   RetryDB 완전 초기화

   ★ 주의: 계정과 전적이 전부 지워진다. 되돌릴 수 없다.

   쓰는 법: SSMS에서 이 파일 열고 F5 한 번. 이거 하나면 끝난다.
            (RetryDB.sql 을 따로 실행할 필요 없다)

   테이블을 지우고 처음부터 다시 만들기 때문에
   "열 이름 'password_hash'이(가) 잘못되었습니다" 류의 오류가 나지 않는다.
   ========================================================================== */

IF DB_ID('RetryDB') IS NULL
    CREATE DATABASE RetryDB;
GO

USE RetryDB;
GO

/* ── 기존 것 제거 (자식 테이블 먼저) ─────────────────────────────────────── */
DROP TABLE IF EXISTS dbo.PlayRecord;
GO
DROP TABLE IF EXISTS dbo.Player;
GO

/* ── 계정 ───────────────────────────────────────────────────────────────── */
CREATE TABLE dbo.Player
(
    player_id         INT              IDENTITY(1,1) NOT NULL,
    nickname          NVARCHAR(16)     NOT NULL,   -- 한글 6자 / 영문 12자 제한과 맞춤
    password_salt     UNIQUEIDENTIFIER NULL,       -- 계정마다 다른 소금값
    password_hash     VARBINARY(32)    NULL,       -- SHA-256(salt + 비밀번호)
    created_at        DATETIME2(0)     NOT NULL CONSTRAINT DF_Player_created DEFAULT SYSDATETIME(),
    last_login_at     DATETIME2(0)     NULL,
    total_runs        INT              NOT NULL CONSTRAINT DF_Player_runs    DEFAULT 0,
    total_extractions INT              NOT NULL CONSTRAINT DF_Player_ext     DEFAULT 0,

    CONSTRAINT PK_Player      PRIMARY KEY (player_id),
    CONSTRAINT UQ_Player_nick UNIQUE (nickname)    -- ★ 닉네임 중복 방지
);
GO

/* ── 전적 ───────────────────────────────────────────────────────────────── */
CREATE TABLE dbo.PlayRecord
(
    record_id     BIGINT       IDENTITY(1,1) NOT NULL,
    player_id     INT          NOT NULL,
    session_id    INT          NOT NULL,
    extracted     BIT          NOT NULL,      -- 1=탈출 성공, 0=사망/중도이탈
    play_seconds  INT          NOT NULL,
    monster_kills INT          NOT NULL,
    player_kills  INT          NOT NULL,
    recorded_at   DATETIME2(0) NOT NULL CONSTRAINT DF_Rec_at DEFAULT SYSDATETIME(),

    CONSTRAINT PK_PlayRecord    PRIMARY KEY (record_id),
    CONSTRAINT FK_PlayRecord_Pl FOREIGN KEY (player_id) REFERENCES dbo.Player(player_id)
);
GO

CREATE INDEX IX_PlayRecord_player ON dbo.PlayRecord(player_id, recorded_at DESC);
GO

/* ==========================================================================
   함수 / 저장 프로시저
   ========================================================================== */

/* 해시 계산 규칙을 한 곳에만 둔다. 로그인/가입/재설정이 모두 이걸 쓴다. */
CREATE OR ALTER FUNCTION dbo.fn_PasswordHash
(
    @salt     UNIQUEIDENTIFIER,
    @password NVARCHAR(64)
)
RETURNS VARBINARY(32)
AS
BEGIN
    RETURN HASHBYTES('SHA2_256', CONVERT(NVARCHAR(36), @salt) + @password);
END
GO

/* 로그인.  result : 0=성공, 1=없는 닉네임, 2=비밀번호 불일치 */
CREATE OR ALTER PROCEDURE dbo.usp_LoginPlayer
    @nickname NVARCHAR(16),
    @password NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @id INT, @salt UNIQUEIDENTIFIER, @hash VARBINARY(32);
    SELECT @id = player_id, @salt = password_salt, @hash = password_hash
    FROM   dbo.Player WHERE nickname = @nickname;

    IF @id IS NULL
    BEGIN
        SELECT 1 AS result, 0 AS player_id, 0 AS total_runs, 0 AS total_extractions;
        RETURN;
    END

    IF @hash IS NULL          -- 비밀번호가 없던 구 계정은 이번 값으로 설정
    BEGIN
        SET @salt = NEWID();
        UPDATE dbo.Player
        SET    password_salt = @salt,
               password_hash = dbo.fn_PasswordHash(@salt, @password)
        WHERE  player_id = @id;
    END
    ELSE IF dbo.fn_PasswordHash(@salt, @password) <> @hash
    BEGIN
        SELECT 2 AS result, 0 AS player_id, 0 AS total_runs, 0 AS total_extractions;
        RETURN;
    END

    UPDATE dbo.Player SET last_login_at = SYSDATETIME() WHERE player_id = @id;

    SELECT 0 AS result, player_id, total_runs, total_extractions
    FROM   dbo.Player WHERE player_id = @id;
END
GO

/* 회원가입.  result : 0=성공, 1=닉네임 중복 */
CREATE OR ALTER PROCEDURE dbo.usp_RegisterPlayer
    @nickname NVARCHAR(16),
    @password NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.Player WHERE nickname = @nickname)
    BEGIN
        SELECT 1 AS result, 0 AS player_id;
        RETURN;
    END

    DECLARE @salt UNIQUEIDENTIFIER = NEWID();

    BEGIN TRY
        INSERT INTO dbo.Player(nickname, password_salt, password_hash)
        VALUES (@nickname, @salt, dbo.fn_PasswordHash(@salt, @password));

        SELECT 0 AS result, CAST(SCOPE_IDENTITY() AS INT) AS player_id;
    END TRY
    BEGIN CATCH
        SELECT 1 AS result, 0 AS player_id;   -- 동시 가입으로 UNIQUE에 걸린 경우
    END CATCH
END
GO

/* 비밀번호 재설정.  result : 0=성공, 1=없는 닉네임 */
CREATE OR ALTER PROCEDURE dbo.usp_ResetPassword
    @nickname NVARCHAR(16),
    @password NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @id INT;
    SELECT @id = player_id FROM dbo.Player WHERE nickname = @nickname;
    IF @id IS NULL
    BEGIN
        SELECT 1 AS result;
        RETURN;
    END

    DECLARE @salt UNIQUEIDENTIFIER = NEWID();
    UPDATE dbo.Player
    SET    password_salt = @salt,
           password_hash = dbo.fn_PasswordHash(@salt, @password)
    WHERE  player_id = @id;

    SELECT 0 AS result;
END
GO

/* 전적 1줄 기록 + 계정 누적 통계 갱신. 세션 종료 시 플레이어마다 1회. */
CREATE OR ALTER PROCEDURE dbo.usp_InsertPlayRecord
    @nickname      NVARCHAR(16),
    @session_id    INT,
    @extracted     BIT,
    @play_seconds  INT,
    @monster_kills INT,
    @player_kills  INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @id INT;
    SELECT @id = player_id FROM dbo.Player WHERE nickname = @nickname;
    IF @id IS NULL RETURN;        -- 모르는 닉네임이면 조용히 버린다

    BEGIN TRANSACTION;

    INSERT INTO dbo.PlayRecord
        (player_id, session_id, extracted, play_seconds, monster_kills, player_kills)
    VALUES
        (@id, @session_id, @extracted, @play_seconds, @monster_kills, @player_kills);

    UPDATE dbo.Player
    SET total_runs        = total_runs + 1,
        total_extractions = total_extractions + CASE WHEN @extracted = 1 THEN 1 ELSE 0 END
    WHERE player_id = @id;

    COMMIT TRANSACTION;
END
GO

/* 로비에서 "플레이 전적" 띄울 때 쓰는 조회 */
CREATE OR ALTER PROCEDURE dbo.usp_GetPlayerStats
    @nickname NVARCHAR(16)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.total_runs,
           p.total_extractions,
           ISNULL(SUM(r.monster_kills), 0) AS monster_kills,
           ISNULL(SUM(r.player_kills), 0)  AS player_kills,
           ISNULL(MAX(r.play_seconds), 0)  AS best_seconds
    FROM   dbo.Player p
    LEFT   JOIN dbo.PlayRecord r ON r.player_id = p.player_id
    WHERE  p.nickname = @nickname
    GROUP  BY p.total_runs, p.total_extractions;
END
GO

/* ── 결과 확인 ──────────────────────────────────────────────────────────── */
PRINT '초기화 완료';
SELECT name AS 테이블 FROM sys.tables ORDER BY name;
SELECT name AS 프로시저 FROM sys.procedures ORDER BY name;
SELECT COUNT(*) AS 남은계정 FROM dbo.Player;
GO
