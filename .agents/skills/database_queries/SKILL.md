---
name: database_queries
description: Instructions for checking the database for facts via SQL queries run by the user.
---
# Database Queries

When you (the AI) need to check the database for facts or retrieve data, you cannot query the database directly. Instead, you must:

1. **Generate an SQL query** that retrieves the necessary information. Keep in mind the database is **SQL Server Express**.
2. **Ask the user to run the query** and copy-paste the results back to you.
3. **Include specific instructions** for the user on how to execute the query using the **latest version of SQL Server Management Studio (SSMS)**, which is their preferred tool.

## Example Instructions for the User
When presenting the query to the user, you can include instructions similar to the following:

> Please run the following SQL query in SQL Server Management Studio (SSMS) against our SQL Server Express database and paste the results back here:
> 
> ```sql
> SELECT TOP 10 * FROM YourTable;
> ```
> 
> **How to run this in SSMS:**
> 1. Open SQL Server Management Studio and connect to your SQL Server Express instance.
> 2. Open a **New Query** window (Ctrl+N).
> 3. Ensure you have the correct database selected in the Available Databases dropdown (or add `USE [DatabaseName];` at the top).
> 4. Paste the query above into the query window.
> 5. Click **Execute** (or press F5).
> 6. Right-click the results grid, select **Select All** (Ctrl+A), then right-click and select **Copy with Headers** (Ctrl+Shift+C).
> 7. Paste the copied results in your reply.

## SQL Server Considerations
- Use T-SQL syntax specific to Microsoft SQL Server (e.g., `TOP` instead of `LIMIT`, `ISNULL` or `COALESCE`, `GETDATE()`).
- SQL Server Express has limitations on database size and resources, but the query syntax is identical to standard SQL Server.
