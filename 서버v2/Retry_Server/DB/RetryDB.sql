/* ==========================================================================
   Retry 게임 DB 스키마
   SSMS에서 이 파일을 열고 [실행(F5)] 한 번이면 끝난다. 여러 번 실행해도 안전하다.

   테이블 2개
     Player     : 계정. 닉네임이 UNIQUE라 중복이 DB 차원에서 막힌다.
     PlayRecord : 판마다 남는 전적. 세션 종료 시 서버가 한 줄씩 넣는다.

   서버는 테이블을 직접 건드리지 않고 아래 저장 프로시저만 호출한다.
   (SQL 문자열을 서버 코드에서 조립하지 않으므로 SQL 인젝션이 원천 차단된다)
   ========================================================================== */

IF DB_ID('RetryDB') IS NULL
    CREATE DATABASE RetryDB;
GO

USE RetryDB;
GO

/* ── 계정 ───────────────────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.Player', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Player
    (
        player_id         INT           IDENTITY(1,1) NOT NULL,
        nickname          NVARCHAR(16)  NOT NULL,      -- 한글 6자 / 영문 12자 제한과 맞춤
        created_at        DATETIME2(0)  NOT NULL CONSTRAINT DF_Player_created DEFAULT SYSDATETIME(),
        last_login_at     DATETIME2(0)  NULL,
        total_runs        INT           NOT NULL CONSTRAINT DF_Player_runs    DEFAULT 0,
        total_extractions INT           NOT NULL CONSTRAINT DF_Player_ext     DEFAULT 0,

        CONSTRAINT PK_Player        PRIMARY KEY (player_id),
        CONSTRAINT UQ_Player_nick   UNIQUE (nickname)   -- ★ 닉네임 중복 방지
    );
END
GO

/* ── 전적 ───────────────────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.PlayRecord', 'U') IS NULL
BEGIN
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

        CONSTRAINT PK_PlayRecord     PRIMARY KEY (record_id),
        CONSTRAINT FK_PlayRecord_Pl  FOREIGN KEY (player_id) REFERENCES dbo.Player(player_id)
    );
END
GO

-- "내 최근 전적" 조회용.
-- CREATE INDEX 는 IF 로 감싸도 실행 전에 검증돼서 "이미 있습니다" 오류가 난다.
-- 동적 SQL로 넘기면 IF가 false일 때 파싱 자체를 하지 않아 조용히 넘어간다.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_PlayRecord_player'
                 AND object_id = OBJECT_ID('dbo.PlayRecord'))
    EXEC sp_executesql
        N'CREATE INDEX IX_PlayRecord_player ON dbo.PlayRecord(player_id, recorded_at DESC)';
GO

/* ── 비밀번호 (나중에 추가된 컬럼) ─────────────────────────────────────────
   평문은 저장하지 않는다. 계정마다 salt를 만들고 SHA-256으로 해시해 둔다.
   해시는 SQL Server의 HASHBYTES가 해주므로 서버 C++ 코드에는 암호 로직이 없다. */
IF COL_LENGTH('dbo.Player', 'password_salt') IS NULL
    EXEC sp_executesql N'ALTER TABLE dbo.Player ADD password_salt UNIQUEIDENTIFIER NULL';
GO
IF COL_LENGTH('dbo.Player', 'password_hash') IS NULL
    EXEC sp_executesql N'ALTER TABLE dbo.Player ADD password_hash VARBINARY(32) NULL';
GO

-- 컬럼이 실제로 생겼는지 확인하고, 없으면 여기서 멈춘다.
-- (없는 채로 아래 프로시저를 만들면 "열 이름이 잘못되었습니다"가 쏟아진다)
IF COL_LENGTH('dbo.Player', 'password_hash') IS NULL
   OR COL_LENGTH('dbo.Player', 'password_salt') IS NULL
BEGIN
    RAISERROR('비밀번호 컬럼 추가 실패. SQL\초기화.sql 을 대신 실행하세요.', 20, 1) WITH LOG;
END
GO

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

/* ==========================================================================
   저장 프로시저
   ========================================================================== */

/* 로그인. 비밀번호를 검증한다.
   result : 0=성공, 1=없는 닉네임, 2=비밀번호 불일치
   비밀번호가 아직 없는 계정(회원가입 기능 이전에 만들어진 것)은
   이번에 입력한 비밀번호로 설정하고 통과시킨다.                          */
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

    IF @hash IS NULL          -- 구 계정 자동 이행
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
        -- 동시에 같은 닉네임이 들어와 UNIQUE에 걸린 경우
        SELECT 1 AS result, 0 AS player_id;
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

/* 전적 1줄 기록 + 계정 누적 통계 갱신. 세션 종료 시 플레이어마다 1회.
   닉네임으로 찾는 이유: 세션이 끝날 때쯤엔 이미 접속을 끊은 클라도 있어서
   메모리상의 clientId를 믿을 수 없다.                                    */
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

/* 로비에서 "내 전적" 띄울 때 쓰는 조회 */
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

/* ── 확인용 ─────────────────────────────────────────────────────────────── */
-- EXEC dbo.usp_RegisterPlayer N'태규', N'1234';
-- EXEC dbo.usp_LoginPlayer N'태규', N'1234';
-- EXEC dbo.usp_InsertPlayRecord N'태규', 1, 1, 312, 14, 2;
-- EXEC dbo.usp_GetPlayerStats N'태규';
-- SELECT * FROM dbo.Player;
-- SELECT * FROM dbo.PlayRecord ORDER BY record_id DESC;
