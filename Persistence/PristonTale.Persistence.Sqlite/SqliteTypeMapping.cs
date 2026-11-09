using System.Data;
using Dapper;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Sqlite;

public static class SqliteTypeMapping
{
    private static int _initialized;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new BoolHandler());
        SqlMapper.AddTypeHandler(new Int16Handler());
        SqlMapper.AddTypeHandler(new Int32Handler());
        SqlMapper.AddTypeHandler(new SingleHandler());
        SqlMapper.AddTypeHandler(new JsonTextHandler());
        SqlMapper.AddTypeHandler(new DateTimeHandler());
    }

    private sealed class DateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override DateTime Parse(object value) => value switch
        {
            DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(
                DateTime.Parse(value.ToString()!, System.Globalization.CultureInfo.InvariantCulture),
                DateTimeKind.Utc),
        };

        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.DbType = DbType.DateTime;
            parameter.Value = value;
        }
    }

    private sealed class JsonTextHandler : SqlMapper.TypeHandler<JsonText>
    {
        public override JsonText Parse(object value) => new(value?.ToString() ?? string.Empty);

        public override void SetValue(IDbDataParameter parameter, JsonText value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.Value;
        }
    }

    private sealed class BoolHandler : SqlMapper.TypeHandler<bool>
    {
        public override bool Parse(object value) => Convert.ToInt64(value) != 0;

        public override void SetValue(IDbDataParameter parameter, bool value)
        {
            parameter.DbType = DbType.Int64;
            parameter.Value = value ? 1L : 0L;
        }
    }

    private sealed class Int16Handler : SqlMapper.TypeHandler<short>
    {
        public override short Parse(object value) => Convert.ToInt16(value);

        public override void SetValue(IDbDataParameter parameter, short value)
        {
            parameter.DbType = DbType.Int64;
            parameter.Value = (long)value;
        }
    }

    private sealed class Int32Handler : SqlMapper.TypeHandler<int>
    {
        public override int Parse(object value) => Convert.ToInt32(value);

        public override void SetValue(IDbDataParameter parameter, int value)
        {
            parameter.DbType = DbType.Int64;
            parameter.Value = (long)value;
        }
    }

    private sealed class SingleHandler : SqlMapper.TypeHandler<float>
    {
        public override float Parse(object value) => Convert.ToSingle(value);

        public override void SetValue(IDbDataParameter parameter, float value)
        {
            parameter.DbType = DbType.Double;
            parameter.Value = (double)value;
        }
    }
}
