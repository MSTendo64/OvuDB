using System.Collections.Generic;

namespace ovudb.Storage;

/// <summary>
/// Interface for data storage system
/// </summary>
public interface IStorage
{
    /// <summary>
    /// Save table data
    /// </summary>
    void SaveTable(string tableName, Dictionary<string, object> schema, List<Dictionary<string, object>> rows);

    /// <summary>
    /// Load table data
    /// </summary>
    (Dictionary<string, object> schema, List<Dictionary<string, object>> rows)? LoadTable(string tableName);

    /// <summary>
    /// Delete table
    /// </summary>
    void DeleteTable(string tableName);

    /// <summary>
    /// Check if table exists
    /// </summary>
    bool TableExists(string tableName);

    /// <summary>
    /// Get list of all tables
    /// </summary>
    List<string> GetTableNames();

    /// <summary>
    /// Create table dump in JSON format
    /// </summary>
    string CreateDump(string tableName);

    /// <summary>
    /// Restore table from dump
    /// </summary>
    void RestoreFromDump(string tableName, string dumpJson);

    /// <summary>
    /// Create full database dump
    /// </summary>
    string CreateFullDump();

    /// <summary>
    /// Restore database from full dump
    /// </summary>
    void RestoreFromFullDump(string fullDumpJson);

    /// <summary>
    /// Save dump to file
    /// </summary>
    void SaveDumpToFile(string tableName, string dumpJson);

    /// <summary>
    /// Load dump from file
    /// </summary>
    string LoadDumpFromFile(string tableName);
}
