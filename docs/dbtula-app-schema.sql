CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE TABLE "Users" (
        "Id" uuid NOT NULL,
        "Email" text NOT NULL,
        "GoogleId" text NOT NULL,
        "Name" text NOT NULL,
        "Role" text NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE TABLE "Databases" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "DbType" text NOT NULL,
        "Environment" text NOT NULL,
        "ConnectionStringEncrypted" text NOT NULL,
        "IsWriteAccount" boolean NOT NULL,
        "ReadAccountId" uuid,
        "CreatedById" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Databases" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Databases_Users_CreatedById" FOREIGN KEY ("CreatedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE TABLE "ComparisonRuns" (
        "Id" uuid NOT NULL,
        "SourceDbId" uuid NOT NULL,
        "TargetDbId" uuid NOT NULL,
        "Status" text NOT NULL,
        "StartedAt" timestamp with time zone NOT NULL,
        "CompletedAt" timestamp with time zone,
        "InitiatedById" uuid NOT NULL,
        "ResultJson" text,
        "SyncScriptSafe" text,
        "SyncScriptRisky" text,
        "SyncScriptDestructive" text,
        "SummaryJson" text,
        "ErrorMessage" text,
        CONSTRAINT "PK_ComparisonRuns" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ComparisonRuns_Databases_SourceDbId" FOREIGN KEY ("SourceDbId") REFERENCES "Databases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ComparisonRuns_Databases_TargetDbId" FOREIGN KEY ("TargetDbId") REFERENCES "Databases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ComparisonRuns_Users_InitiatedById" FOREIGN KEY ("InitiatedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE TABLE "SyncApplyLogs" (
        "Id" uuid NOT NULL,
        "ComparisonRunId" uuid NOT NULL,
        "AppliedById" uuid NOT NULL,
        "AppliedAt" timestamp with time zone NOT NULL,
        "TargetDbId" uuid NOT NULL,
        "SqlExecuted" text NOT NULL,
        "SuccessCount" integer NOT NULL,
        "FailureCount" integer NOT NULL,
        "ErrorDetails" text,
        CONSTRAINT "PK_SyncApplyLogs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SyncApplyLogs_ComparisonRuns_ComparisonRunId" FOREIGN KEY ("ComparisonRunId") REFERENCES "ComparisonRuns" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SyncApplyLogs_Databases_TargetDbId" FOREIGN KEY ("TargetDbId") REFERENCES "Databases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_SyncApplyLogs_Users_AppliedById" FOREIGN KEY ("AppliedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE INDEX "IX_ComparisonRuns_InitiatedById" ON "ComparisonRuns" ("InitiatedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE INDEX "IX_ComparisonRuns_SourceDbId" ON "ComparisonRuns" ("SourceDbId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE INDEX "IX_ComparisonRuns_TargetDbId" ON "ComparisonRuns" ("TargetDbId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE INDEX "IX_Databases_CreatedById" ON "Databases" ("CreatedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE INDEX "IX_SyncApplyLogs_AppliedById" ON "SyncApplyLogs" ("AppliedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE INDEX "IX_SyncApplyLogs_ComparisonRunId" ON "SyncApplyLogs" ("ComparisonRunId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE INDEX "IX_SyncApplyLogs_TargetDbId" ON "SyncApplyLogs" ("TargetDbId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_GoogleId" ON "Users" ("GoogleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531154143_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260531154143_InitialCreate', '9.0.5');
    END IF;
END $EF$;
COMMIT;

