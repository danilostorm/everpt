using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Postgres;

public sealed class JsonTextNpgsqlHandler : SqlMapper.TypeHandler<JsonText>
{
    public override JsonText Parse(object value) => new(value?.ToString() ?? string.Empty);

    public override void SetValue(IDbDataParameter parameter, JsonText value)
    {
        if (parameter is NpgsqlParameter npgsql)
            npgsql.NpgsqlDbType = NpgsqlDbType.Jsonb;
        else
            parameter.DbType = DbType.String;
        parameter.Value = value.Value;
    }
}
