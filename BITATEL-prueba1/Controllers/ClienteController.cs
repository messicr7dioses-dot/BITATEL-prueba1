using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Web.Mvc;
using BITATEL_prueba1.Models;

namespace BITATEL_prueba1.Controllers
{
    public class ClienteController : Controller
    {
        // =======================================================
        // FUNCIÓN CENTRALIZADA PARA OBTENER EL ID DEL CLIENTE
        // =======================================================
        private int ObtenerIdCliente(string username)
        {
            int idCliente = 0;
            ConexionBD obj = new ConexionBD();
            using (var con = obj.ObtenerConexion())
            {
                // Esta consulta busca el ID del cliente donde el nombre_empresa 
                // coincida exactamente con el username del usuario logueado.
                string query = "SELECT id_cliente FROM Clientes WHERE nombre_empresa = @user";
                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@user", username); // Ahora esto coincide con 'upn', 'hitss', 'stefanini', etc.
                con.Open();
                var res = cmd.ExecuteScalar();
                if (res != null) idCliente = Convert.ToInt32(res);
            }
            return idCliente;
        }

        // =======================================================
        // 1. DASHBOARD
        // =======================================================
        public ActionResult Index()
        {
            if (Session["UsuarioActivo"] == null) return RedirectToAction("Index", "Login");

            Usuario userLogueado = (Usuario)Session["UsuarioActivo"];

            // CORRECCIÓN: Permitimos que pasen tanto el Admin (Rol 1) como el Cliente (Rol 2)
            if (userLogueado.IdRol != 1 && userLogueado.IdRol != 2)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.NombreCompleto = userLogueado.NombreCompleto;
            return View();
        }

        // =======================================================
        // 2. MIS ACTIVOS
        // =======================================================
        public ActionResult MisActivos()
        {
            if (Session["UsuarioActivo"] == null) return RedirectToAction("Index", "Login");
            Usuario user = (Usuario)Session["UsuarioActivo"];
            int idCliente = ObtenerIdCliente(user.Username);

            List<ActivoCliente> lista = new List<ActivoCliente>();
            ConexionBD objConexion = new ConexionBD();

            using (SqlConnection con = objConexion.ObtenerConexion())
            {
                string query = @"SELECT a.etiqueta_activo, c.nombre_categoria, a.marca, a.modelo, 
                                        u.nombre_especifico AS ubicacion, e.nombre_estado
                                 FROM Registro_Alquiler r
                                 JOIN Activos a ON r.id_activo_actual = a.id_activo
                                 JOIN Categorias c ON a.id_categoria = c.id_categoria
                                 JOIN Ubicaciones_Internas u ON a.id_ubicacion = u.id_ubicacion
                                 JOIN Estados_Activo e ON a.id_estado = e.id_estado
                                 WHERE r.id_cliente = @idcli";

                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@idcli", idCliente);
                con.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ActivoCliente
                        {
                            Etiqueta = reader["etiqueta_activo"].ToString(),
                            Categoria = reader["nombre_categoria"].ToString(),
                            Marca = reader["marca"].ToString(),
                            Modelo = reader["modelo"].ToString(),
                            Ubicacion = reader["ubicacion"].ToString(),
                            Estado = reader["nombre_estado"].ToString()
                        });
                    }
                }
            }
            return View(lista);
        }

        // =======================================================
        // 3. CONTRATOS
        // =======================================================
        public ActionResult Contratos()
        {
            if (Session["UsuarioActivo"] == null) return RedirectToAction("Index", "Login");
            Usuario user = (Usuario)Session["UsuarioActivo"];
            int idCliente = ObtenerIdCliente(user.Username);

            List<dynamic> lista = new List<dynamic>();
            ConexionBD obj = new ConexionBD();

            using (SqlConnection con = obj.ObtenerConexion())
            {
                string query = @"SELECT r.id_registro AS NumeroContrato, r.fecha_ingreso AS Inicio,
                                        r.fecha_vencimiento AS Fin, a.etiqueta_activo AS Activo,
                                        CASE WHEN r.fecha_vencimiento >= GETDATE() THEN 'Vigente' ELSE 'Vencido' END AS Estado
                                 FROM Registro_Alquiler r
                                 JOIN Activos a ON r.id_activo_actual = a.id_activo
                                 WHERE r.id_cliente = @idcli";

                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@idcli", idCliente);
                con.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new
                        {
                            Numero = reader["NumeroContrato"].ToString(),
                            Inicio = Convert.ToDateTime(reader["Inicio"]).ToString("dd/MM/yyyy"),
                            Fin = Convert.ToDateTime(reader["Fin"]).ToString("dd/MM/yyyy"),
                            Activo = reader["Activo"].ToString(),
                            Estado = reader["Estado"].ToString()
                        });
                    }
                }
            }
            return View(lista);
        }

        // =======================================================
        // 4. UBICACIONES
        // =======================================================

        public ActionResult Ubicaciones()
        {
            if (Session["UsuarioActivo"] == null) return RedirectToAction("Index", "Login");

            Usuario user = (Usuario)Session["UsuarioActivo"];
            List<UbicacionViewModel> lista = new List<UbicacionViewModel>();
            ConexionBD obj = new ConexionBD();

            using (var con = obj.ObtenerConexion())
            {
                // Si el usuario es Admin(1) o Operador(supongamos rol 3), traemos todo
                bool esAdminOOperador = (user.IdRol == 1 || user.IdRol == 3);

                string query = @"SELECT DISTINCT s.nombre_sede AS Sede, u.nombre_especifico AS Ambiente, u.tipo_ambiente AS Tipo 
                         FROM Ubicaciones_Internas u 
                         JOIN Sedes s ON u.id_sede = s.id_sede";

                if (!esAdminOOperador)
                {
                    // Solo filtramos si es cliente (IdRol == 2)
                    query += @" JOIN Activos a ON a.id_ubicacion = u.id_ubicacion
                        JOIN Registro_Alquiler r ON r.id_activo_actual = a.id_activo
                        WHERE r.id_cliente = @idcli";
                }

                var cmd = new SqlCommand(query, con);
                if (!esAdminOOperador)
                {
                    cmd.Parameters.AddWithValue("@idcli", ObtenerIdCliente(user.Username));
                }

                con.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new UbicacionViewModel
                        {
                            Sede = reader["Sede"].ToString(),
                            Ambiente = reader["Ambiente"].ToString(),
                            Tipo = reader["Tipo"].ToString()
                        });
                    }
                }
            }
            return View(lista);
        }

        // RUTAS ADICIONALES
        public ActionResult Facturacion() { return View(); }
        public ActionResult Solicitudes() { return View(); }
        public ActionResult MiPerfil() { return View(); }
        public ActionResult Configuracion() { return View(); }
    }
}