using ovudb.Core;
using ovudb.SystemDatabase.Models;

namespace ovudb.SystemDatabase;

/// <summary>
/// Service for initializing and managing system database
/// </summary>
public class SystemDatabaseService
{
    private readonly Database _systemDatabase;
    private readonly string _dataDirectory;

    public SystemDatabaseService(string dataDirectory = "ovusys")
    {
        _dataDirectory = dataDirectory;
        
        // Create system database in dedicated directory
        _systemDatabase = new Database("ovusys", dataDirectory: dataDirectory);
        
        // Initialize all system tables
        InitializeSystemTables();
    }

    /// <summary>
    /// Get system database
    /// </summary>
    public Database SystemDatabase => _systemDatabase;

    /// <summary>
    /// Initialize all system tables. Tables are created empty and filled as the system is used.
    /// </summary>
    private void InitializeSystemTables()
    {
        // User and privilege tables: user, db, tables_priv, columns_priv, procs_priv
        InitializeUserTable();
        InitializeDbTable();
        InitializeTablesPrivTable();
        InitializeColumnsPrivTable();
        InitializeProcsPrivTable();
        
        // Time zone tables
        InitializeTimeZoneTables();
        
        // Replication tables
        InitializeReplicationTables();
        
        // Logging tables: general_log, slow_log
        InitializeLogTables();
        
        // Model (table template) tables
        InitializeModelTable();
    }

    /// <summary>
    /// Initialize user table
    /// </summary>
    private void InitializeUserTable()
    {
        var table = _systemDatabase.GetTable<SystemUser>("user");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Username", DataType.String).NotNull().Unique())
            .AddColumn(new Column("PasswordHash", DataType.String).NotNull())
            .AddColumn(new Column("Host", DataType.String).NotNull().WithDefault("%"))
            .AddColumn(new Column("SelectPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("InsertPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("UpdatePriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("DeletePriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("CreatePriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("DropPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("ReloadPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("ShutdownPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("ProcessPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("FilePriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("GrantPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("ReferencesPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("IndexPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("AlterPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("CreatedAt", DataType.DateTime).NotNull())
            .AddColumn(new Column("LastLogin", DataType.DateTime).NotNull());
        
        table.CreateIfNotExists();
    }

    /// <summary>
    /// Initialize db table
    /// </summary>
    private void InitializeDbTable()
    {
        var table = _systemDatabase.GetTable<SystemDb>("db");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Host", DataType.String).NotNull().WithDefault("%"))
            .AddColumn(new Column("Db", DataType.String).NotNull())
            .AddColumn(new Column("User", DataType.String).NotNull())
            .AddColumn(new Column("SelectPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("InsertPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("UpdatePriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("DeletePriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("CreatePriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("DropPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("GrantPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("ReferencesPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("IndexPriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("AlterPriv", DataType.Boolean).NotNull().WithDefault(false));
        
        table.CreateIfNotExists();
    }

    /// <summary>
    /// Initialize tables_priv table
    /// </summary>
    private void InitializeTablesPrivTable()
    {
        var table = _systemDatabase.GetTable<SystemTablesPriv>("tables_priv");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Host", DataType.String).NotNull().WithDefault("%"))
            .AddColumn(new Column("Db", DataType.String).NotNull())
            .AddColumn(new Column("User", DataType.String).NotNull())
            .AddColumn(new Column("TableName", DataType.String).NotNull())
            .AddColumn(new Column("Grantor", DataType.String).NotNull())
            .AddColumn(new Column("Timestamp", DataType.DateTime).NotNull())
            .AddColumn(new Column("TablePriv", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("ColumnPriv", DataType.Boolean).NotNull().WithDefault(false));
        
        table.CreateIfNotExists();
    }

    /// <summary>
    /// Initialize columns_priv table
    /// </summary>
    private void InitializeColumnsPrivTable()
    {
        var table = _systemDatabase.GetTable<SystemColumnsPriv>("columns_priv");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Host", DataType.String).NotNull().WithDefault("%"))
            .AddColumn(new Column("Db", DataType.String).NotNull())
            .AddColumn(new Column("User", DataType.String).NotNull())
            .AddColumn(new Column("TableName", DataType.String).NotNull())
            .AddColumn(new Column("ColumnName", DataType.String).NotNull())
            .AddColumn(new Column("Timestamp", DataType.DateTime).NotNull())
            .AddColumn(new Column("ColumnPriv", DataType.Boolean).NotNull().WithDefault(false));
        
        table.CreateIfNotExists();
    }

    /// <summary>
    /// Initialize procs_priv table
    /// </summary>
    private void InitializeProcsPrivTable()
    {
        var table = _systemDatabase.GetTable<SystemProcsPriv>("procs_priv");
        table
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Host", DataType.String).NotNull().WithDefault("%"))
            .AddColumn(new Column("Db", DataType.String).NotNull())
            .AddColumn(new Column("User", DataType.String).NotNull())
            .AddColumn(new Column("RoutineName", DataType.String).NotNull())
            .AddColumn(new Column("RoutineType", DataType.String).NotNull().WithDefault("PROCEDURE"))
            .AddColumn(new Column("Grantor", DataType.String).NotNull())
            .AddColumn(new Column("Timestamp", DataType.DateTime).NotNull())
            .AddColumn(new Column("ProcPriv", DataType.Boolean).NotNull().WithDefault(false));
        
        table.CreateIfNotExists();
    }

    /// <summary>
    /// Initialize time zone tables
    /// </summary>
    private void InitializeTimeZoneTables()
    {
        // time_zone
        var timeZoneTable = _systemDatabase.GetTable<SystemTimeZone>("time_zone");
        timeZoneTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("TimeZoneId", DataType.Integer).NotNull())
            .AddColumn(new Column("UseLeapSeconds", DataType.Boolean).NotNull().WithDefault(false));
        timeZoneTable.CreateIfNotExists();

        // time_zone_name
        var timeZoneNameTable = _systemDatabase.GetTable<SystemTimeZoneName>("time_zone_name");
        timeZoneNameTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull().Unique())
            .AddColumn(new Column("TimeZoneId", DataType.Integer).NotNull());
        timeZoneNameTable.CreateIfNotExists();

        // time_zone_transition
        var timeZoneTransitionTable = _systemDatabase.GetTable<SystemTimeZoneTransition>("time_zone_transition");
        timeZoneTransitionTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("TimeZoneId", DataType.Integer).NotNull())
            .AddColumn(new Column("TransitionTime", DataType.Long).NotNull())
            .AddColumn(new Column("TransitionTypeId", DataType.Integer).NotNull());
        timeZoneTransitionTable.CreateIfNotExists();
    }

    /// <summary>
    /// Initialize replication tables
    /// </summary>
    private void InitializeReplicationTables()
    {
        // gtid_executed
        var gtidTable = _systemDatabase.GetTable<SystemGtidExecuted>("gtid_executed");
        gtidTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("SourceUuid", DataType.String).NotNull())
            .AddColumn(new Column("IntervalStart", DataType.Long).NotNull())
            .AddColumn(new Column("IntervalEnd", DataType.Long).NotNull());
        gtidTable.CreateIfNotExists();

        // slave_master_info
        var slaveMasterInfoTable = _systemDatabase.GetTable<SystemSlaveMasterInfo>("slave_master_info");
        slaveMasterInfoTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("NumberOfLines", DataType.Integer).NotNull().WithDefault(0))
            .AddColumn(new Column("MasterLogName", DataType.String).NotNull())
            .AddColumn(new Column("MasterLogPos", DataType.Long).NotNull())
            .AddColumn(new Column("Host", DataType.String).NotNull())
            .AddColumn(new Column("UserName", DataType.String).NotNull())
            .AddColumn(new Column("UserPassword", DataType.String).NotNull())
            .AddColumn(new Column("Port", DataType.Integer).NotNull())
            .AddColumn(new Column("ConnectRetry", DataType.Integer).NotNull().WithDefault(60))
            .AddColumn(new Column("EnabledSsl", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("SslCa", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("SslCapath", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("SslCert", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("SslCipher", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("SslKey", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("SslVerifyServerCert", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("Heartbeat", DataType.Double).NotNull().WithDefault(30.0))
            .AddColumn(new Column("Bind", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("IgnoredServerIds", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("Uuid", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("RetryCount", DataType.Long).NotNull().WithDefault(86400))
            .AddColumn(new Column("SslCrl", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("SslCrlpath", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("EnabledAutoPosition", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("ChannelName", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("TlsVersion", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("PublicKeyPath", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("GetPublicKey", DataType.Boolean).NotNull().WithDefault(false))
            .AddColumn(new Column("NetworkNamespace", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("MasterCompressionAlgorithm", DataType.Integer).NotNull().WithDefault(0))
            .AddColumn(new Column("MasterZstdCompressionLevel", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("TlsCiphersuites", DataType.Integer).NotNull().WithDefault(0))
            .AddColumn(new Column("SourceConnectionAutoFailover", DataType.String).NotNull().WithDefault(""))
            .AddColumn(new Column("GtidOnly", DataType.Integer).NotNull().WithDefault(0));
        slaveMasterInfoTable.CreateIfNotExists();
    }

    /// <summary>
    /// Initialize logging tables
    /// </summary>
    private void InitializeLogTables()
    {
        // general_log
        var generalLogTable = _systemDatabase.GetTable<SystemGeneralLog>("general_log");
        generalLogTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("EventTime", DataType.DateTime).NotNull())
            .AddColumn(new Column("UserHost", DataType.String).NotNull())
            .AddColumn(new Column("ThreadId", DataType.Long).NotNull())
            .AddColumn(new Column("ServerId", DataType.Long).NotNull())
            .AddColumn(new Column("CommandType", DataType.String).NotNull())
            .AddColumn(new Column("Argument", DataType.String).NotNull());
        generalLogTable.CreateIfNotExists();

        // slow_log
        var slowLogTable = _systemDatabase.GetTable<SystemSlowLog>("slow_log");
        slowLogTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("StartTime", DataType.DateTime).NotNull())
            .AddColumn(new Column("UserHost", DataType.String).NotNull())
            .AddColumn(new Column("QueryTime", DataType.Long).NotNull())
            .AddColumn(new Column("LockTime", DataType.Long).NotNull())
            .AddColumn(new Column("RowsSent", DataType.Long).NotNull())
            .AddColumn(new Column("RowsExamined", DataType.Long).NotNull())
            .AddColumn(new Column("Db", DataType.String).NotNull())
            .AddColumn(new Column("LastInsertId", DataType.Long).NotNull())
            .AddColumn(new Column("InsertId", DataType.Long).NotNull())
            .AddColumn(new Column("ServerId", DataType.Long).NotNull())
            .AddColumn(new Column("SqlText", DataType.String).NotNull())
            .AddColumn(new Column("ThreadId", DataType.Long).NotNull());
        slowLogTable.CreateIfNotExists();
    }

    /// <summary>
    /// Get user table
    /// </summary>
    public Table<SystemUser> GetUserTable()
    {
        return _systemDatabase.GetTable<SystemUser>("user");
    }

    /// <summary>
    /// Get db table
    /// </summary>
    public Table<SystemDb> GetDbTable()
    {
        return _systemDatabase.GetTable<SystemDb>("db");
    }

    /// <summary>
    /// Get tables_priv table
    /// </summary>
    public Table<SystemTablesPriv> GetTablesPrivTable()
    {
        return _systemDatabase.GetTable<SystemTablesPriv>("tables_priv");
    }

    /// <summary>
    /// Get columns_priv table
    /// </summary>
    public Table<SystemColumnsPriv> GetColumnsPrivTable()
    {
        return _systemDatabase.GetTable<SystemColumnsPriv>("columns_priv");
    }

    /// <summary>
    /// Get procs_priv table
    /// </summary>
    public Table<SystemProcsPriv> GetProcsPrivTable()
    {
        return _systemDatabase.GetTable<SystemProcsPriv>("procs_priv");
    }

    /// <summary>
    /// Get general log table
    /// </summary>
    public Table<SystemGeneralLog> GetGeneralLogTable()
    {
        return _systemDatabase.GetTable<SystemGeneralLog>("general_log");
    }

    /// <summary>
    /// Get slow log table
    /// </summary>
    public Table<SystemSlowLog> GetSlowLogTable()
    {
        return _systemDatabase.GetTable<SystemSlowLog>("slow_log");
    }

    /// <summary>
    /// Initialize models table
    /// </summary>
    private void InitializeModelTable()
    {
        var modelTable = _systemDatabase.GetTable<Model>("models");
        modelTable
            .AddColumn(new Column("Id", DataType.Integer).PrimaryKey().AutoIncrement())
            .AddColumn(new Column("Name", DataType.String).NotNull())
            .AddColumn(new Column("ModelType", DataType.String).NotNull().WithDefault("perm"))
            .AddColumn(new Column("FieldsJson", DataType.String).NotNull())
            .AddColumn(new Column("CreatedAt", DataType.DateTime).NotNull())
            .AddColumn(new Column("UpdatedAt", DataType.DateTime).NotNull());
        modelTable.CreateIfNotExists();
    }

    /// <summary>
    /// Get models table
    /// </summary>
    public Table<Model> GetModelTable()
    {
        return _systemDatabase.GetTable<Model>("models");
    }
}
