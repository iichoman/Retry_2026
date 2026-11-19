#pragma once
// RetryDB.sql에서 뽑은 스키마 (주석·DB 만들기·USE 제외). 같은 내용이다.
// 서버가 DB에 붙을 때마다 순서대로 실행해 표·프로시저를 서버 버전에 맞춘다 (여러 번 해도 안전).
// RetryDB.sql을 고치면 이 파일도 같이 고친다.
static const wchar_t* const kRetryDbSchema[] = {
    LR"SQL(SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;)SQL",
    LR"SQL(IF OBJECT_ID('dbo.Player', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Player
    (
        player_id         INT           IDENTITY(1,1) NOT NULL,
        nickname          NVARCHAR(16)  NOT NULL,
        created_at        DATETIME2(0)  NOT NULL CONSTRAINT DF_Player_created DEFAULT SYSDATETIME(),
        last_login_at     DATETIME2(0)  NULL,
        total_runs        INT           NOT NULL CONSTRAINT DF_Player_runs    DEFAULT 0,
        total_extractions INT           NOT NULL CONSTRAINT DF_Player_ext     DEFAULT 0,
        CONSTRAINT PK_Player        PRIMARY KEY (player_id),
        CONSTRAINT UQ_Player_nick   UNIQUE (nickname)
    );
END)SQL",
    LR"SQL(IF OBJECT_ID('dbo.PlayRecord', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlayRecord
    (
        record_id     BIGINT       IDENTITY(1,1) NOT NULL,
        player_id     INT          NOT NULL,
        session_id    INT          NOT NULL,
        extracted     BIT          NOT NULL,
        play_seconds  INT          NOT NULL,
        monster_kills INT          NOT NULL,
        player_kills  INT          NOT NULL,
        recorded_at   DATETIME2(0) NOT NULL CONSTRAINT DF_Rec_at DEFAULT SYSDATETIME(),
        CONSTRAINT PK_PlayRecord     PRIMARY KEY (record_id),
        CONSTRAINT FK_PlayRecord_Pl  FOREIGN KEY (player_id) REFERENCES dbo.Player(player_id)
    );
END)SQL",
    LR"SQL(IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_PlayRecord_player'
                 AND object_id = OBJECT_ID('dbo.PlayRecord'))
    EXEC sp_executesql
        N'CREATE INDEX IX_PlayRecord_player ON dbo.PlayRecord(player_id, recorded_at DESC)';)SQL",
    LR"SQL(IF COL_LENGTH('dbo.Player', 'password_salt') IS NULL
    EXEC sp_executesql N'ALTER TABLE dbo.Player ADD password_salt UNIQUEIDENTIFIER NULL';)SQL",
    LR"SQL(IF COL_LENGTH('dbo.Player', 'password_hash') IS NULL
    EXEC sp_executesql N'ALTER TABLE dbo.Player ADD password_hash VARBINARY(32) NULL';)SQL",
    LR"SQL(CREATE OR ALTER FUNCTION dbo.fn_PasswordHash
(
    @salt     UNIQUEIDENTIFIER,
    @password NVARCHAR(64)
)
RETURNS VARBINARY(32)
AS
BEGIN
    RETURN HASHBYTES('SHA2_256', CONVERT(NVARCHAR(36), @salt) + @password);
END)SQL",
    LR"SQL(CREATE OR ALTER PROCEDURE dbo.usp_LoginPlayer
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
    IF @hash IS NULL
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
END)SQL",
    LR"SQL(CREATE OR ALTER PROCEDURE dbo.usp_RegisterPlayer
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
        SELECT 1 AS result, 0 AS player_id;
    END CATCH
END)SQL",
    LR"SQL(CREATE OR ALTER PROCEDURE dbo.usp_ResetPassword
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
END)SQL",
    LR"SQL(CREATE OR ALTER PROCEDURE dbo.usp_InsertPlayRecord
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
    IF @id IS NULL RETURN;
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
END)SQL",
    LR"SQL(CREATE OR ALTER PROCEDURE dbo.usp_GetPlayerStats
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
END)SQL",
    LR"SQL(IF OBJECT_ID('dbo.Stash', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Stash
    (
        player_id      INT          NOT NULL,
        credits        BIGINT       NOT NULL CONSTRAINT DF_Stash_credits DEFAULT 300,
        weapon_hash    INT          NOT NULL CONSTRAINT DF_Stash_weapon  DEFAULT 0,
        item_hash      INT          NOT NULL CONSTRAINT DF_Stash_item    DEFAULT 0,
        run_token      VARCHAR(64)  NULL,
        last_run_token VARCHAR(64)  NULL,
        carried_weapon INT          NOT NULL CONSTRAINT DF_Stash_cw DEFAULT 0,
        carried_item   INT          NOT NULL CONSTRAINT DF_Stash_ci DEFAULT 0,
        carried_count  INT          NOT NULL CONSTRAINT DF_Stash_cc DEFAULT 0,
        updated_at     DATETIME2(0) NOT NULL CONSTRAINT DF_Stash_at DEFAULT SYSDATETIME(),
        CONSTRAINT PK_Stash         PRIMARY KEY (player_id),
        CONSTRAINT CK_Stash_credits CHECK (credits BETWEEN 0 AND 1000000000)
    );
END)SQL",
    LR"SQL(IF OBJECT_ID('dbo.FK_Stash_Player', 'F') IS NULL
BEGIN
    BEGIN TRY
        ALTER TABLE dbo.Stash ADD CONSTRAINT FK_Stash_Player
            FOREIGN KEY (player_id) REFERENCES dbo.Player(player_id);
    END TRY
    BEGIN CATCH
        PRINT 'FK_Stash_Player skipped: ' + ERROR_MESSAGE();
    END CATCH
END)SQL",
    LR"SQL(IF OBJECT_ID('dbo.StashItem', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StashItem
    (
        player_id  INT NOT NULL,
        item_hash  INT NOT NULL,
        item_count INT NOT NULL,
        CONSTRAINT PK_StashItem       PRIMARY KEY (player_id, item_hash),
        CONSTRAINT FK_StashItem_Stash FOREIGN KEY (player_id) REFERENCES dbo.Stash(player_id),
        CONSTRAINT CK_StashItem_count CHECK (item_count BETWEEN 1 AND 999999)
    );
END)SQL",
    LR"SQL(CREATE OR ALTER PROCEDURE dbo.usp_LoadStash
    @player_id INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Stash WHERE player_id = @player_id)
        INSERT INTO dbo.Stash(player_id) VALUES (@player_id);
    SELECT credits, weapon_hash, item_hash,
           ISNULL(run_token, '')      AS run_token,
           ISNULL(last_run_token, '') AS last_run_token,
           carried_weapon, carried_item, carried_count
    FROM   dbo.Stash WHERE player_id = @player_id;
    SELECT item_hash, item_count
    FROM   dbo.StashItem WHERE player_id = @player_id
    ORDER  BY item_hash;
END)SQL",
    LR"SQL(CREATE OR ALTER PROCEDURE dbo.usp_SaveStash
    @player_id      INT,
    @credits        BIGINT,
    @weapon_hash    INT,
    @item_hash      INT,
    @run_token      VARCHAR(64),
    @last_run_token VARCHAR(64),
    @carried_weapon INT,
    @carried_item   INT,
    @carried_count  INT,
    @items          VARCHAR(8000)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @x XML = CAST(@items AS XML);
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.Stash
        SET    credits        = @credits,
               weapon_hash    = @weapon_hash,
               item_hash      = @item_hash,
               run_token      = NULLIF(@run_token, ''),
               last_run_token = NULLIF(@last_run_token, ''),
               carried_weapon = @carried_weapon,
               carried_item   = @carried_item,
               carried_count  = @carried_count,
               updated_at     = SYSDATETIME()
        WHERE  player_id = @player_id;
        IF @@ROWCOUNT = 0
            INSERT INTO dbo.Stash(player_id, credits, weapon_hash, item_hash, run_token, last_run_token,
                                  carried_weapon, carried_item, carried_count)
            VALUES (@player_id, @credits, @weapon_hash, @item_hash,
                    NULLIF(@run_token, ''), NULLIF(@last_run_token, ''),
                    @carried_weapon, @carried_item, @carried_count);
        DELETE FROM dbo.StashItem WHERE player_id = @player_id;
        INSERT INTO dbo.StashItem(player_id, item_hash, item_count)
        SELECT @player_id, t.i.value('@h', 'INT'), t.i.value('@c', 'INT')
        FROM   @x.nodes('/i') AS t(i);
        COMMIT TRANSACTION;
        SELECT 0 AS result;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END)SQL"
};
