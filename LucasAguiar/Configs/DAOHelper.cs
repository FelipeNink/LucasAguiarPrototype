using MySqlConnector;

namespace LucasAguiar.Configs
{
    public static class DAOHelper
    {

        public static string GetString(MySqlDataReader reader, string column_name)
        {
            string text = string.Empty;
            int ordinal = reader.GetOrdinal(column_name);

            if (!reader.IsDBNull(ordinal))
                text = reader.GetString(ordinal);

            return text;
        }

        public static double GetDouble(MySqlDataReader reader, string column_name)
        {
            double value = 0.0;
            int ordinal = reader.GetOrdinal(column_name);

            if (!reader.IsDBNull(ordinal))
                value = reader.GetDouble(ordinal);

            return value;
        }

        public static DateTime? GetDateTime(MySqlDataReader reader, string column_name)
        {
            DateTime? value = null;
            int ordinal = reader.GetOrdinal(column_name);

            if (!reader.IsDBNull(ordinal))
                value = reader.GetDateTime(ordinal);

            return value;
        }

        public static bool IsNull(MySqlDataReader reader, string column_name)
        {
            return reader.IsDBNull(reader.GetOrdinal(column_name));
        }

        /// <summary>
        /// DELETE barrado por chave estrangeira: o registro ainda e usado
        /// em outra tabela (venda, compra, assinatura...).
        /// </summary>
        public static bool EhRegistroEmUso(MySqlException ex)
        {
            return ex.ErrorCode == MySqlErrorCode.RowIsReferenced2
                || ex.ErrorCode == MySqlErrorCode.RowIsReferenced;
        }
    }
}