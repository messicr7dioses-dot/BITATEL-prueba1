
using System;
using System.Configuration;
using System.Data.SqlClient;

namespace BITATEL_prueba1.Models
{
    public class ConexionBD
    {
        // Este método lee el Web.config y te devuelve una conexión lista para usar
        public SqlConnection ObtenerConexion()
        {
            string cadenaConexion = ConfigurationManager.ConnectionStrings["BitatelConexion"].ConnectionString;
            return new SqlConnection(cadenaConexion);
        }
    }
}