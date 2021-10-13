-- connect to user database
-- hacker1 for Function, hacker2 for testing of data masking

CREATE USER hacker1 FOR LOGIN hacker1;

ALTER ROLE db_datareader ADD MEMBER hacker1;
ALTER ROLE db_datawriter ADD MEMBER hacker1;

CREATE USER hacker2 FOR LOGIN hacker2;

ALTER ROLE db_datareader ADD MEMBER hacker2;

GRANT UNMASK TO hacker1;

GO
