-- Create the application's database role. Idempotent — re-run on every deploy.
--
-- The app deliberately does NOT connect as the Postgres superuser. A superuser role turns any
-- SQL-injection bug into code execution inside the database container (COPY ... FROM PROGRAM is
-- superuser-only), which would be a much shorter path from "bug in AnkiLearner" to "foothold on
-- a box that also hosts an unrelated application".
--
-- It still needs DDL rights, because EF Core applies migrations at startup — hence ownership of
-- the database and of the public schema, rather than SUPERUSER.
--
-- Invoked as:  psql -v ANKILEARNER_PASSWORD="..." -f 01-app-user.sql

SELECT 'CREATE ROLE ankilearner LOGIN'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ankilearner')
\gexec

ALTER ROLE ankilearner WITH LOGIN PASSWORD :'ANKILEARNER_PASSWORD';

ALTER DATABASE ankilearner OWNER TO ankilearner;
ALTER SCHEMA public OWNER TO ankilearner;
GRANT ALL ON SCHEMA public TO ankilearner;

-- No CREATEDB, no CREATEROLE, no SUPERUSER, no REPLICATION.
