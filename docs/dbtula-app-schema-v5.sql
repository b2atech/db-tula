START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601173354_Batch4Features') THEN
    ALTER TABLE "Profiles" ADD "CronExpression" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601173354_Batch4Features') THEN
    ALTER TABLE "ComparisonRuns" ADD "BatchRunId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601173354_Batch4Features') THEN
    CREATE TABLE "BatchRuns" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "TotalRuns" integer NOT NULL,
        "CompletedRuns" integer NOT NULL,
        "FailedRuns" integer NOT NULL,
        "StartedAt" timestamp with time zone NOT NULL,
        "CompletedAt" timestamp with time zone,
        "InitiatedById" uuid NOT NULL,
        CONSTRAINT "PK_BatchRuns" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_BatchRuns_Users_InitiatedById" FOREIGN KEY ("InitiatedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601173354_Batch4Features') THEN
    CREATE INDEX "IX_BatchRuns_InitiatedById" ON "BatchRuns" ("InitiatedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601173354_Batch4Features') THEN
    CREATE INDEX "IX_BatchRuns_StartedAt" ON "BatchRuns" ("StartedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601173354_Batch4Features') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260601173354_Batch4Features', '9.0.5');
    END IF;
END $EF$;
COMMIT;

