pipeline {
    agent {
        docker {
            image 'mcr.microsoft.com/dotnet/sdk:9.0'
            args  '-v /root/.nuget:/root/.nuget'  // cache NuGet packages across builds
        }
    }

    options {
        buildDiscarder(logRotator(daysToKeepStr: '7', numToKeepStr: '20'))
        timeout(time: 30, unit: 'MINUTES')
    }

    triggers {
        cron('30 18 * * *') // midnight IST
    }

    environment {
        // QA (54.37.159.71) — existing services
        COMMONDB_QA     = credentials('CONNECTIONSTRINGS__COMMONDB_QA')
        COMMUNITYDB_QA  = credentials('CONNECTIONSTRINGS__COMMUNITYDB_QA')
        INVENTORYDB_QA  = credentials('CONNECTIONSTRINGS__INVENTORYDB_QA')
        PAYROLLDB_QA    = credentials('CONNECTIONSTRINGS__PAYROLLDB_QA')
        PURCHASEDB_QA   = credentials('CONNECTIONSTRINGS__PURCHASEDB_QA')
        SALESDB_QA      = credentials('CONNECTIONSTRINGS__SALESDB_QA')

        // PROD (51.79.156.217) — existing services
        COMMONDB_PROD     = credentials('CONNECTIONSTRINGS__COMMONDB_PROD')
        COMMUNITYDB_PROD  = credentials('CONNECTIONSTRINGS__COMMUNITYDB_PROD')
        INVENTORYDB_PROD  = credentials('CONNECTIONSTRINGS__INVENTORYDB_PROD')
        PAYROLLDB_PROD    = credentials('CONNECTIONSTRINGS__PAYROLLDB_PROD')
        PURCHASEDB_PROD   = credentials('CONNECTIONSTRINGS__PURCHASEDB_PROD')
        SALESDB_PROD      = credentials('CONNECTIONSTRINGS__SALESDB_PROD')

        // TEST — existing services
        COMMONDB_TEST     = credentials('CONNECTIONSTRINGS__COMMONDB_TEST')
        COMMUNITYDB_TEST  = credentials('CONNECTIONSTRINGS__COMMUNITYDB_TEST')
        INVENTORYDB_TEST  = credentials('CONNECTIONSTRINGS__INVENTORYDB_TEST')
        PAYROLLDB_TEST    = credentials('CONNECTIONSTRINGS__PAYROLLDB_TEST')
        PURCHASEDB_TEST   = credentials('CONNECTIONSTRINGS__PURCHASEDB_TEST')
        SALESDB_TEST      = credentials('CONNECTIONSTRINGS__SALESDB_TEST')

        DO_HOST       = credentials('DO_HOST')
        DO_USER       = credentials('DO_USER')
        DO_SSH_KEY_ID = 'DO_SSH_KEY'

        DBTULA = './publish/B2A.DbTula.Cli'
    }

    stages {

        stage('Checkout') {
            steps {
                checkout([
                    $class: 'GitSCM',
                    branches: [[name: '*/main']],
                    userRemoteConfigs: [[
                        url: 'https://github.com/b2atech/db-tula.git',
                        credentialsId: 'bharat-mane-git-personal-token'
                    ]]
                ])
            }
        }

        stage('Build & Publish') {
            steps {
                sh '''
                    dotnet restore src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj

                    dotnet publish src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj \
                        --configuration Release \
                        --runtime linux-x64 \
                        --self-contained true \
                        -p:PublishSingleFile=true \
                        -o ./publish/

                    chmod +x ./publish/B2A.DbTula.Cli
                '''
            }
        }

        stage('Unit Tests') {
            steps {
                sh '''
                    dotnet test tests/B2A.DbTula.Core.Tests/B2A.DbTula.Core.Tests.csproj \
                        --configuration Release \
                        --logger "junit;LogFilePath=../../test-results/unit-tests.xml" \
                        || true
                '''
            }
            post {
                always {
                    junit allowEmptyResults: true, testResults: 'test-results/unit-tests.xml'
                }
            }
        }

        // Run QA-vs-PROD and QA-vs-TEST comparisons in parallel
        stage('Schema Comparisons') {
            parallel {

                stage('QA vs PROD') {
                    steps {
                        script {
                            def driftDetected = false
                            def services = [
                                [id: 'COMMON',    label: 'Common'],
                                [id: 'COMMUNITY', label: 'Community'],
                                [id: 'INVENTORY', label: 'Inventory'],
                                [id: 'PAYROLL',   label: 'Payroll'],
                                [id: 'PURCHASE',  label: 'Purchase'],
                                [id: 'SALES',     label: 'Sales'],
                            ]

                            sh 'mkdir -p gh-pages/qa-vs-prod'

                            services.each { svc ->
                                def qaConn   = env["${svc.id}DB_QA"]
                                def prodConn = env["${svc.id}DB_PROD"]
                                def outFile  = "gh-pages/qa-vs-prod/${svc.id.toLowerCase()}.html"

                                def exitCode = sh(returnStatus: true, script: """
                                    ${DBTULA} \
                                        --source  "${qaConn}"   --source-label  QA \
                                        --target  "${prodConn}" --target-label  PROD \
                                        --title   "${svc.label} Schema (QA vs PROD)" \
                                        --out     "${outFile}" \
                                        --generate-sync \
                                        --sync-out "gh-pages/qa-vs-prod/${svc.id.toLowerCase()}-sync.sql" \
                                        --fail-on-drift
                                """)

                                if (exitCode == 2) {
                                    echo "🚨 ${svc.label}: objects missing in PROD (exit ${exitCode})"
                                    driftDetected = true
                                } else if (exitCode == 1) {
                                    echo "⚠️  ${svc.label}: schema mismatches detected (exit ${exitCode})"
                                    driftDetected = true
                                } else if (exitCode >= 3) {
                                    error("❌ ${svc.label} QA-vs-PROD comparison failed (exit ${exitCode})")
                                }
                            }

                            // Optional new services — skip gracefully if credentials missing
                            def newServices = [
                                [id: 'PAYMENT',  label: 'Payment'],
                                [id: 'DOCUMENT', label: 'Document'],
                                [id: 'AGENT',    label: 'Agent'],
                                [id: 'EINVOICE', label: 'EInvoice'],
                            ]
                            newServices.each { svc ->
                                try {
                                    withCredentials([
                                        string(credentialsId: "CONNECTIONSTRINGS__${svc.id}DB_QA",   variable: 'SVC_QA'),
                                        string(credentialsId: "CONNECTIONSTRINGS__${svc.id}DB_PROD", variable: 'SVC_PROD'),
                                    ]) {
                                        def exitCode = sh(returnStatus: true, script: """
                                            ${DBTULA} \
                                                --source  "\$SVC_QA"   --source-label QA \
                                                --target  "\$SVC_PROD" --target-label PROD \
                                                --title   "${svc.label} Schema (QA vs PROD)" \
                                                --out     "gh-pages/qa-vs-prod/${svc.id.toLowerCase()}.html" \
                                                --fail-on-drift
                                        """)
                                        if (exitCode > 0 && exitCode < 3) driftDetected = true
                                    }
                                } catch (e) {
                                    echo "⏭  Skipping ${svc.label} — credentials not configured: ${e.message}"
                                }
                            }

                            if (driftDetected) {
                                currentBuild.result = 'UNSTABLE'
                                echo '⚠️  QA vs PROD drift detected — build marked UNSTABLE'
                            }
                        }
                    }
                }

                stage('QA vs TEST') {
                    steps {
                        script {
                            def services = [
                                [id: 'COMMON',    label: 'Common'],
                                [id: 'COMMUNITY', label: 'Community'],
                                [id: 'INVENTORY', label: 'Inventory'],
                                [id: 'PAYROLL',   label: 'Payroll'],
                                [id: 'PURCHASE',  label: 'Purchase'],
                                [id: 'SALES',     label: 'Sales'],
                            ]

                            sh 'mkdir -p gh-pages/qa-vs-test'

                            services.each { svc ->
                                def qaConn   = env["${svc.id}DB_QA"]
                                def testConn = env["${svc.id}DB_TEST"]

                                sh """
                                    ${DBTULA} \
                                        --source  "${qaConn}"   --source-label  QA \
                                        --target  "${testConn}" --target-label  TEST \
                                        --title   "${svc.label} Schema (QA vs TEST)" \
                                        --out     "gh-pages/qa-vs-test/${svc.id.toLowerCase()}.html" \
                                        || true
                                """
                                // QA-vs-TEST does not fail the build — TEST env can legitimately lag
                            }
                        }
                    }
                }

            } // end parallel
        }

        stage('Archive Reports') {
            steps {
                archiveArtifacts artifacts: 'gh-pages/**/*.html,gh-pages/**/*.sql',
                    allowEmptyArchive: true
            }
        }

        stage('Deploy to OVH') {
            steps {
                sshagent(credentials: ['DO_SSH_KEY']) {
                    sh '''
                        scp -o StrictHostKeyChecking=no -r gh-pages/* \
                            ${DO_USER}@${DO_HOST}:/var/www/dbtula-site
                    '''
                }
            }
        }

    }

    post {
        always {
            cleanWs()
        }
        unstable {
            echo '⚠️  Schema drift detected in QA vs PROD — review reports on the OVH site'
            // Uncomment to add Slack notification:
            // slackSend channel: '#deploys', color: 'warning',
            //   message: "dbtula: Schema drift detected on ${env.JOB_NAME} — ${env.BUILD_URL}"
        }
        failure {
            echo '❌ Comparison pipeline failed — check logs for connection or build errors'
        }
        success {
            echo '✅ All schema comparisons completed — no drift detected'
        }
    }
}
