using System.Text;
using System.Text.Json;

namespace ovudb.Configuration;

/// <summary>
/// Configuration loader from YAML file
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// Load configuration from file
    /// </summary>
    public static ServerConfig LoadFromFile(string configPath = "ovudbc.yml")
    {
        if (!File.Exists(configPath))
        {
            // If file does not exist, create default configuration
            var defaultConfig = new ServerConfig();
            SaveToFile(defaultConfig, configPath);
            Console.WriteLine($"Config file {configPath} not found. Created file with default settings.");
            return defaultConfig;
        }

        try
        {
            var yamlContent = File.ReadAllText(configPath, Encoding.UTF8);
            return ParseYaml(yamlContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading configuration from {configPath}: {ex.Message}");
            Console.WriteLine("Using default settings.");
            return new ServerConfig();
        }
    }

    /// <summary>
    /// Save configuration to file
    /// </summary>
    public static void SaveToFile(ServerConfig config, string configPath = "ovudbc.yml")
    {
        try
        {
            var yamlContent = GenerateYaml(config);
            File.WriteAllText(configPath, yamlContent, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving configuration to {configPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse YAML configuration (simplified implementation)
    /// </summary>
    private static ServerConfig ParseYaml(string yamlContent)
    {
        var config = new ServerConfig();
        var lines = yamlContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            // Remove quotes if present
            if (value.StartsWith('"') && value.EndsWith('"'))
                value = value[1..^1];
            else if (value.StartsWith('\'') && value.EndsWith('\''))
                value = value[1..^1];
            
            // Additional trim for safety
            key = key.Trim();
            value = value.Trim();

            var keyLower = key.ToLowerInvariant();
            
            switch (keyLower)
            {
                case "port":
                    if (int.TryParse(value, out var port))
                        config.Port = port;
                    break;
                case "mysqlport":
                    if (int.TryParse(value, out var mysqlPort))
                    {
                        config.MySqlPort = mysqlPort;
                        Console.WriteLine($"[Config] Loaded MySQL port: {mysqlPort}");
                    }
                    else if (value.Equals("null", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(value))
                    {
                        config.MySqlPort = null;
                        Console.WriteLine("[Config] MySQL port set to null");
                    }
                    else
                    {
                        Console.WriteLine($"[Config] Warning: Could not parse mysqlPort value: '{value}'");
                    }
                    break;
                case "datadirectory":
                    config.DataDirectory = value;
                    break;
                case "maxconnections":
                    if (int.TryParse(value, out var maxConnections))
                        config.MaxConnections = maxConnections;
                    break;
                case "idletimeoutminutes":
                    if (int.TryParse(value, out var idleTimeout))
                        config.IdleTimeoutMinutes = idleTimeout;
                    break;
                case "bufferpoolsize":
                    if (int.TryParse(value, out var bufferPoolSize))
                        config.BufferPoolSize = bufferPoolSize;
                    break;
                case "pagesize":
                    if (int.TryParse(value, out var pageSize))
                        config.PageSize = pageSize;
                    break;
                case "querycachemaxentries":
                    if (int.TryParse(value, out var queryCacheMaxEntries))
                        config.QueryCacheMaxEntries = queryCacheMaxEntries;
                    break;
                case "querycachettlminutes":
                    if (int.TryParse(value, out var queryCacheTtl))
                        config.QueryCacheTtlMinutes = queryCacheTtl;
                    break;
            }
        }

        return config;
    }

    /// <summary>
    /// Generate YAML configuration
    /// </summary>
    private static string GenerateYaml(ServerConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# OvuDB Server Configuration");
        sb.AppendLine();
        sb.AppendLine("# Server port (1-65535)");
        sb.AppendLine($"port: {config.Port}");
        sb.AppendLine();
        sb.AppendLine("# MySQL-compatible server port (null = disabled, 3306 = default MySQL port)");
        if (config.MySqlPort.HasValue)
            sb.AppendLine($"mysqlPort: {config.MySqlPort}");
        else
            sb.AppendLine("# mysqlPort: null");
        sb.AppendLine();
        sb.AppendLine("# Data directory");
        sb.AppendLine($"dataDirectory: \"{config.DataDirectory}\"");
        sb.AppendLine();
        sb.AppendLine("# Max concurrent connections");
        sb.AppendLine($"maxConnections: {config.MaxConnections}");
        sb.AppendLine();
        sb.AppendLine("# Idle connection timeout (minutes)");
        sb.AppendLine($"idleTimeoutMinutes: {config.IdleTimeoutMinutes}");
        sb.AppendLine();
        sb.AppendLine("# Buffer pool size (number of pages)");
        sb.AppendLine($"bufferPoolSize: {config.BufferPoolSize}");
        sb.AppendLine();
        sb.AppendLine("# Page size in bytes");
        sb.AppendLine($"pageSize: {config.PageSize}");
        sb.AppendLine();
        sb.AppendLine("# Max query cache entries");
        sb.AppendLine($"queryCacheMaxEntries: {config.QueryCacheMaxEntries}");
        sb.AppendLine();
        sb.AppendLine("# Query cache TTL (minutes)");
        sb.AppendLine($"queryCacheTtlMinutes: {config.QueryCacheTtlMinutes}");
        
        return sb.ToString();
    }
}
