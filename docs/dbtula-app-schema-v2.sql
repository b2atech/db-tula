START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    ALTER TABLE "ComparisonRuns" ADD "ProfileId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE TABLE "AllowedEmails" (
        "Id" uuid NOT NULL,
        "Email" text NOT NULL,
        "AddedById" uuid NOT NULL,
        "AddedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_AllowedEmails" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AllowedEmails_Users_AddedById" FOREIGN KEY ("AddedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE TABLE "DriftMetrics" (
        "Id" uuid NOT NULL,
        "ComparisonRunId" uuid NOT NULL,
        "RunDate" timestamp with time zone NOT NULL,
        "ObjectType" text NOT NULL,
        "MatchCount" integer NOT NULL,
        "MismatchCount" integer NOT NULL,
        "MissingInTargetCount" integer NOT NULL,
        "MissingInSourceCount" integer NOT NULL,
        CONSTRAINT "PK_DriftMetrics" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DriftMetrics_ComparisonRuns_ComparisonRunId" FOREIGN KEY ("ComparisonRunId") REFERENCES "ComparisonRuns" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE TABLE "Profiles" (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "Description" text,
        "SourceDbId" uuid NOT NULL,
        "TargetDbId" uuid NOT NULL,
        "IgnoreOwnership" boolean NOT NULL,
        "CreatedById" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Profiles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Profiles_Databases_SourceDbId" FOREIGN KEY ("SourceDbId") REFERENCES "Databases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Profiles_Databases_TargetDbId" FOREIGN KEY ("TargetDbId") REFERENCES "Databases" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Profiles_Users_CreatedById" FOREIGN KEY ("CreatedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE TABLE "SyncStatements" (
        "Id" uuid NOT NULL,
        "ComparisonRunId" uuid NOT NULL,
        "Category" text NOT NULL,
        "ObjectType" text NOT NULL,
        "ObjectName" text NOT NULL,
        "Sql" text NOT NULL,
        "Comment" text NOT NULL,
        "OrderIndex" integer NOT NULL,
        "IsApproved" boolean NOT NULL,
        "IsApplied" boolean NOT NULL,
        "AppliedAt" timestamp with time zone,
        "AppliedById" uuid,
        CONSTRAINT "PK_SyncStatements" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SyncStatements_ComparisonRuns_ComparisonRunId" FOREIGN KEY ("ComparisonRunId") REFERENCES "ComparisonRuns" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SyncStatements_Users_AppliedById" FOREIGN KEY ("AppliedById") REFERENCES "Users" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE INDEX "IX_ComparisonRuns_ProfileId" ON "ComparisonRuns" ("ProfileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE INDEX "IX_AllowedEmails_AddedById" ON "AllowedEmails" ("AddedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE UNIQUE INDEX "IX_AllowedEmails_Email" ON "AllowedEmails" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE INDEX "IX_DriftMetrics_ComparisonRunId_ObjectType" ON "DriftMetrics" ("ComparisonRunId", "ObjectType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE INDEX "IX_DriftMetrics_RunDate" ON "DriftMetrics" ("RunDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE INDEX "IX_Profiles_CreatedById" ON "Profiles" ("CreatedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE INDEX "IX_Profiles_SourceDbId" ON "Profiles" ("SourceDbId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE INDEX "IX_Profiles_TargetDbId" ON "Profiles" ("TargetDbId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE INDEX "IX_SyncStatements_AppliedById" ON "SyncStatements" ("AppliedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    CREATE INDEX "IX_SyncStatements_ComparisonRunId_Category" ON "SyncStatements" ("ComparisonRunId", "Category");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    ALTER TABLE "ComparisonRuns" ADD CONSTRAINT "FK_ComparisonRuns_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "Profiles" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260531165841_AddPhase2Tables') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260531165841_AddPhase2Tables', '9.0.5');
    END IF;
END $EF$;
COMMIT;

