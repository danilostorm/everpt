using System.Data;
using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Infrastructure;
using FluentMigrator.Model;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Migrations;

namespace PristonTale.Tests;

// The characters row as the migrations declare it against the record Dapper builds from it: on Postgres a column comes
// back in its own type (smallint as short) and Dapper finds the record's constructor only when every type matches.
internal static class CharacterColumnTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("every numeric and flag column of characters has the record's own type, as Postgres returns it", ColumnsAsync);
    }

    private static readonly Dictionary<DbType, Type> ReadAs = new()
    {
        [DbType.Int16] = typeof(short),
        [DbType.Int32] = typeof(int),
        [DbType.Int64] = typeof(long),
        [DbType.Boolean] = typeof(bool),
        [DbType.Single] = typeof(float)
    };

    private static Task ColumnsAsync()
    {
        var columns = new Dictionary<string, DbType>();
        var migrations = typeof(M013Baseline).Assembly.GetTypes()
            .Where(type => typeof(Migration).IsAssignableFrom(type) && !type.IsAbstract && type.GetCustomAttributes(typeof(MigrationAttribute), false).Length > 0)
            .OrderBy(type => ((MigrationAttribute)type.GetCustomAttributes(typeof(MigrationAttribute), false)[0]).Version);
        foreach (var type in migrations)
        {
            var context = new MigrationContext(EmptyDatabase.Instance, null!, null);
            ((IMigration)Activator.CreateInstance(type)!).GetUpExpressions(context);
            foreach (var expression in context.Expressions)
            {
                IEnumerable<ColumnDefinition> added = expression switch
                {
                    CreateTableExpression { TableName: "characters" } table => table.Columns,
                    CreateColumnExpression { TableName: "characters" } column => [column.Column],
                    AlterColumnExpression { TableName: "characters" } column => [column.Column],
                    _ => []
                };
                foreach (var column in added.Where(column => column.Type is { } dbType && ReadAs.ContainsKey(dbType)))
                {
                    columns[column.Name] = column.Type!.Value;
                }
            }
        }

        var parameters = typeof(CharacterRecord).GetConstructors().Single().GetParameters();
        var checkedColumns = 0;
        foreach (var parameter in parameters)
        {
            var name = string.Concat(parameter.Name!.Select((letter, index) => char.IsUpper(letter) && index > 0 ? $"_{char.ToLowerInvariant(letter)}" : $"{char.ToLowerInvariant(letter)}"));
            if (columns.TryGetValue(name, out var dbType))
            {
                checkedColumns++;
                Assert.True(parameter.ParameterType == ReadAs[dbType], $"characters.{name} is {dbType} but the record takes {parameter.ParameterType.Name}");
            }
        }

        Assert.True(checkedColumns >= 25 && columns.ContainsKey("help_pet"), $"only {checkedColumns} columns were compared");
        return Task.CompletedTask;
    }

    // A database with nothing in it yet, as the migrations first meet it on Postgres.
    private sealed class EmptyDatabase : IQuerySchema
    {
        public static readonly EmptyDatabase Instance = new();

        public string DatabaseType => "Postgres";

        public IList<string> DatabaseTypeAliases => [];

        public bool SchemaExists(string schemaName) => false;

        public bool TableExists(string schemaName, string tableName) => false;

        public bool ColumnExists(string schemaName, string tableName, string columnName) => false;

        public bool ConstraintExists(string schemaName, string tableName, string constraintName) => false;

        public bool IndexExists(string schemaName, string tableName, string indexName) => false;

        public bool SequenceExists(string schemaName, string sequenceName) => false;

        public bool DefaultValueExists(string schemaName, string tableName, string columnName, object defaultValue) => false;
    }
}
