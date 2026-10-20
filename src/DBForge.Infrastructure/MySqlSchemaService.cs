using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace DBForge.Infrastructure;

public sealed class MySqlSchemaService
{
    public async Task<List<string>> GetDatabasesAsync(SqlConnectionOptions options)
    {
        var result = new List<string>();
        var connStr = options.BuildConnectionString(includeDatabase: false);

        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        const string sql = "SELECT name FROM sys.databases WHERE state_desc = 'ONLINE' ORDER BY name;";
        using var cmd = new SqlCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public async Task<List<string>> GetTablesAsync(SqlConnectionOptions options)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Database)) return result;

        var connStr = options.BuildConnectionString(includeDatabase: true);
        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        const string sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME;";
        using var cmd = new SqlCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public async Task<DataTable> ExecuteQueryAsync(SqlConnectionOptions options, string query)
    {
        var dt = new DataTable();
        var connStr = options.BuildConnectionString(includeDatabase: true);
        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(query, conn);
        using var adapter = new SqlDataAdapter(cmd);
        adapter.Fill(dt);
        return dt;
    }
}