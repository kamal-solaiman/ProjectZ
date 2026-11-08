-- Read-only acceptance evidence. Run on the real primary server.
USE [ProjectZ];
GO
SELECT SERVERPROPERTY('ProductVersion') AS ProductVersion,
       SERVERPROPERTY('ProductLevel') AS ProductLevel,
       SERVERPROPERTY('Edition') AS Edition,
       @@SERVERNAME AS ServerName;
SELECT name, compatibility_level FROM sys.databases WHERE name = N'ProjectZ';
SELECT COUNT(*) AS UserTableCount FROM sys.tables WHERE is_ms_shipped = 0;
SELECT c.name, TYPE_NAME(c.user_type_id) AS DataType, c.max_length, c.is_nullable, c.is_identity
FROM sys.columns AS c WHERE c.object_id = OBJECT_ID(N'dbo.Users') ORDER BY c.column_id;
SELECT Id, Username, DisplayName, IsActive, CreatedAt FROM dbo.Users;
GO
