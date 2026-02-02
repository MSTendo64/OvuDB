# MySQL-совместимость OvuDB

OvuDB теперь поддерживает MySQL Wire Protocol, что позволяет подключаться к базе данных с помощью стандартных MySQL-клиентов, включая:
- `mysql` (командная строка)
- `mysql-connector-python` (Python)
- `mysql2` (Node.js)
- `mysql-connector-java` (Java)
- И другие клиенты, поддерживающие MySQL протокол

## Настройка

### 1. Включение MySQL-совместимости

Отредактируйте файл конфигурации `ovudbc.yml`:

```yaml
# OvuDB native protocol port
port: 47015

# MySQL-compatible server port (null = disabled, 3306 = default MySQL port)
mysqlPort: 3306

# Остальные настройки...
dataDirectory: "data"
maxConnections: 100
```

### 2. Запуск сервера

```bash
dotnet run --project sovudb
```

Сервер запустится на двух портах:
- **47015** - OvuDB native protocol (JSON)
- **3306** - MySQL-compatible protocol (если включен)

## Использование

### Python (mysql-connector-python)

```python
import mysql.connector

# Подключение к OvuDB через MySQL-протокол
conn = mysql.connector.connect(
    host='localhost',
    port=3306,
    user='admin',
    password='admin',
    database='your_database'
)

cursor = conn.cursor()

# Выполнение запросов
cursor.execute("SELECT * FROM users")
results = cursor.fetchall()

for row in results:
    print(row)

cursor.close()
conn.close()
```

### Командная строка (mysql client)

```bash
mysql -h localhost -P 3306 -u admin -p
# Введите пароль: admin

mysql> USE your_database;
mysql> SELECT * FROM users;
mysql> INSERT INTO users (name, email) VALUES ('John', 'john@example.com');
mysql> EXIT;
```

### Node.js (mysql2)

```javascript
const mysql = require('mysql2/promise');

async function connect() {
    const connection = await mysql.createConnection({
        host: 'localhost',
        port: 3306,
        user: 'admin',
        password: 'admin',
        database: 'your_database'
    });

    const [rows] = await connection.execute('SELECT * FROM users');
    console.log(rows);

    await connection.end();
}

connect();
```

## Поддерживаемые команды

### SQL-запросы
- `SELECT` - выборка данных
- `INSERT` - вставка данных
- `UPDATE` - обновление данных
- `DELETE` - удаление данных
- `CREATE TABLE` - создание таблиц
- `DROP TABLE` - удаление таблиц

### Служебные команды
- `USE database` - выбор базы данных
- `SHOW DATABASES` - список баз данных
- `SHOW TABLES` - список таблиц
- `SELECT DATABASE()` - текущая база данных
- `SELECT VERSION()` - версия сервера
- `SELECT USER()` - текущий пользователь

## Ограничения

1. **Аутентификация**: Используется упрощенная аутентификация. В production рекомендуется улучшить безопасность.

2. **Поддерживаемые типы данных**: 
   - INTEGER
   - STRING/VARCHAR
   - DOUBLE/FLOAT
   - BOOLEAN
   - NULL

3. **Не поддерживается**:
   - Prepared statements (COM_STMT_PREPARE)
   - Транзакции (частично)
   - Некоторые расширенные функции MySQL

## Безопасность

⚠️ **Важно**: Текущая реализация использует упрощенную аутентификацию. Для production использования рекомендуется:
- Использовать SSL/TLS
- Улучшить алгоритм аутентификации
- Настроить firewall
- Использовать сильные пароли

## Отладка

Если возникают проблемы с подключением:

1. Проверьте, что MySQL-порт включен в конфигурации
2. Убедитесь, что порт не занят другим процессом
3. Проверьте логи сервера на наличие ошибок
4. Убедитесь, что пользователь существует в системе OvuDB

## Примеры использования

### Создание таблицы и вставка данных

```python
import mysql.connector

conn = mysql.connector.connect(
    host='localhost',
    port=3306,
    user='admin',
    password='admin'
)

cursor = conn.cursor()

# Создание базы данных
cursor.execute("CREATE DATABASE IF NOT EXISTS testdb")
cursor.execute("USE testdb")

# Создание таблицы
cursor.execute("""
    CREATE TABLE users (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        name STRING NOT NULL,
        email STRING
    )
""")

# Вставка данных
cursor.execute("INSERT INTO users (name, email) VALUES (%s, %s)", 
               ("John Doe", "john@example.com"))
conn.commit()

# Выборка данных
cursor.execute("SELECT * FROM users")
for row in cursor.fetchall():
    print(row)

cursor.close()
conn.close()
```

## Технические детали

MySQL-совместимость реализована через:
- **MySqlProtocol.cs** - обработка MySQL Wire Protocol
- **MySqlServer.cs** - MySQL-совместимый сервер
- **MySqlQueryHandler.cs** - преобразование MySQL-запросов в OvuDB
- **MySqlConnection.cs** - управление MySQL-соединениями

Сервер обрабатывает MySQL-пакеты и преобразует их в вызовы OvuDB API, обеспечивая прозрачную совместимость с MySQL-клиентами.

