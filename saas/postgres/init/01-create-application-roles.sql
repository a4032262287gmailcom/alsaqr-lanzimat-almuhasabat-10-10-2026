\getenv app_password POSTGRES_APP_PASSWORD
\getenv migration_password POSTGRES_MIGRATION_PASSWORD

SELECT format('CREATE ROLE alsaqr_app LOGIN PASSWORD %L', :'app_password') \gexec
SELECT format('CREATE ROLE alsaqr_migrator LOGIN PASSWORD %L', :'migration_password') \gexec

ALTER DATABASE alsaqr_saas OWNER TO alsaqr_migrator;
ALTER SCHEMA public OWNER TO alsaqr_migrator;
GRANT CONNECT, TEMPORARY ON DATABASE alsaqr_saas TO alsaqr_app;
GRANT USAGE ON SCHEMA public TO alsaqr_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO alsaqr_app;
ALTER DEFAULT PRIVILEGES FOR ROLE alsaqr_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO alsaqr_app;
