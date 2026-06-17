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
        // SEGURIDAD: OBTENER ID DEL CLIENTE LOGUEADO
        // =======================================================
        private int ObtenerIdCliente()
        {
            if (Session["UsuarioActivo"] == null) return 0;
            Usuario user = (Usuario)Session["UsuarioActivo"];
            int idCliente = 0;
            using (var con = new ConexionBD().ObtenerConexion())
            {
                string query = "SELECT id_cliente FROM Usuarios WHERE id_usuario = @idUsu";
                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@idUsu", user.IdUsuario);
                con.Open();
                var res = cmd.ExecuteScalar();
                if (res != DBNull.Value && res != null) idCliente = Convert.ToInt32(res);
            }
            return idCliente;
        }

        // =======================================================
        // 1. DASHBOARD CLIENTE
        // =======================================================
        public ActionResult Index()
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0) return RedirectToAction("Index", "Login");

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Registro_Alquiler r INNER JOIN Activos a ON r.id_activo_actual = a.id_activo WHERE r.id_cliente = @id AND a.id_estado = 2", con))
                {
                    cmd.Parameters.AddWithValue("@id", idCliente);
                    ViewBag.TotalActivos = (int)cmd.ExecuteScalar();
                }

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Sedes WHERE id_cliente = @id", con))
                {
                    cmd.Parameters.AddWithValue("@id", idCliente);
                    ViewBag.TotalSedes = (int)cmd.ExecuteScalar();
                }

                string sqlDeuda = @"SELECT ISNULL(SUM(DATEDIFF(day, r.fecha_vencimiento, GETDATE()) * ISNULL(r.multa_diaria_usd, 1.00)), 0) 
                                    FROM Registro_Alquiler r 
                                    INNER JOIN Activos a ON r.id_activo_actual = a.id_activo 
                                    WHERE r.id_cliente = @id AND a.id_estado = 2 AND r.fecha_vencimiento < GETDATE()";
                using (var cmd = new SqlCommand(sqlDeuda, con))
                {
                    cmd.Parameters.AddWithValue("@id", idCliente);
                    ViewBag.TotalDeuda = Convert.ToDecimal(cmd.ExecuteScalar());
                }
            }
            return View();
        }

        // =======================================================
        // 2. MIS ACTIVOS / INVENTARIO (CON ACCIONES DE CLIENTE)
        // =======================================================
        public class ActivoClienteItem
        {
            public int IdActivo { get; set; }
            public string Etiqueta { get; set; }
            public string Categoria { get; set; }
            public string Marca { get; set; }
            public string Modelo { get; set; }
            public string Serie { get; set; }
            public string Ubicacion { get; set; }
            public string Estado { get; set; }
        }

        public ActionResult MisActivos()
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0) return RedirectToAction("Index", "Login");

            var lista = new List<ActivoClienteItem>();
            using (SqlConnection con = new ConexionBD().ObtenerConexion())
            {
                string query = @"SELECT a.id_activo, a.etiqueta_activo, c.nombre_categoria, a.marca, a.modelo, a.serie, 
                                        u.nombre_especifico AS ubicacion, e.nombre_estado
                                 FROM Registro_Alquiler r
                                 JOIN Activos a ON r.id_activo_actual = a.id_activo
                                 JOIN Categorias c ON a.id_categoria = c.id_categoria
                                 JOIN Ubicaciones_Internas u ON a.id_ubicacion = u.id_ubicacion
                                 JOIN Estados_Activo e ON a.id_estado = e.id_estado
                                 WHERE r.id_cliente = @idcli AND a.id_estado = 2";

                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@idcli", idCliente);
                con.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ActivoClienteItem
                        {
                            IdActivo = Convert.ToInt32(reader["id_activo"]),
                            Etiqueta = reader["etiqueta_activo"].ToString(),
                            Categoria = reader["nombre_categoria"].ToString(),
                            Marca = reader["marca"].ToString(),
                            Modelo = reader["modelo"].ToString(),
                            Serie = reader["serie"].ToString(),
                            Ubicacion = reader["ubicacion"].ToString(),
                            Estado = reader["nombre_estado"].ToString()
                        });
                    }
                }
            }
            return View(lista);
        }

        [HttpGet]
        public JsonResult ObtenerMisUbicaciones()
        {
            int idCliente = ObtenerIdCliente();
            var lista = new List<object>();
            if (idCliente == 0) return Json(lista, JsonRequestBehavior.AllowGet);

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"SELECT u.id_ubicacion, s.nombre_sede + ' - ' + u.nombre_especifico AS lugar 
                               FROM Ubicaciones_Internas u 
                               INNER JOIN Sedes s ON u.id_sede = s.id_sede 
                               WHERE s.id_cliente = @idCli";

                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@idCli", idCliente);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new { id = reader["id_ubicacion"].ToString(), texto = reader["lugar"].ToString() });
                        }
                    }
                }
            }
            return Json(lista, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult EjecutarTrasladoCliente(List<int> ids, int idUbicacionDestino)
        {
            int idCliente = ObtenerIdCliente();
            Usuario user = (Usuario)Session["UsuarioActivo"];
            if (idCliente == 0 || ids == null || ids.Count == 0) return Json(new { success = false });

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                using (var trans = con.BeginTransaction())
                {
                    try
                    {
                        foreach (int id in ids)
                        {
                            int idOrigen = 0;
                            using (var cmdUbi = new SqlCommand($"SELECT id_ubicacion FROM Activos WHERE id_activo = {id}", con, trans))
                            {
                                var res = cmdUbi.ExecuteScalar();
                                if (res != DBNull.Value && res != null) idOrigen = Convert.ToInt32(res);
                            }

                            new SqlCommand($"UPDATE Activos SET id_ubicacion = {idUbicacionDestino} WHERE id_activo = {id}", con, trans).ExecuteNonQuery();
                            new SqlCommand($"INSERT INTO Movimientos (id_activo, id_usuario, id_ubicacion_origen, id_ubicacion_destino, motivo, fecha_movimiento) VALUES ({id}, {user.IdUsuario}, {idOrigen}, {idUbicacionDestino}, 'TRASLADO CLIENTE', GETDATE())", con, trans).ExecuteNonQuery();
                        }
                        trans.Commit();
                        return Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return Json(new { success = false, message = ex.Message });
                    }
                }
            }
        }

        [HttpPost]
        public JsonResult ReportarAveria(List<int> ids, string motivo)
        {
            int idCliente = ObtenerIdCliente();
            Usuario user = (Usuario)Session["UsuarioActivo"];
            if (idCliente == 0 || ids == null || ids.Count == 0) return Json(new { success = false });

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                using (var trans = con.BeginTransaction())
                {
                    try
                    {
                        foreach (int id in ids)
                        {
                            int idActual = 0;
                            using (var cmdUbi = new SqlCommand($"SELECT id_ubicacion FROM Activos WHERE id_activo = {id}", con, trans))
                            {
                                var res = cmdUbi.ExecuteScalar();
                                if (res != DBNull.Value && res != null) idActual = Convert.ToInt32(res);
                            }

                            // Cambiamos el estado a 3 (Averiado/Dañado) para que el Admin lo atienda
                            new SqlCommand($"UPDATE Activos SET id_estado = 3 WHERE id_activo = {id}", con, trans).ExecuteNonQuery();

                            string observacionSegura = motivo.Replace("'", "''");
                            new SqlCommand($"INSERT INTO Movimientos (id_activo, id_usuario, id_ubicacion_origen, id_ubicacion_destino, motivo, fecha_movimiento, observacion) VALUES ({id}, {user.IdUsuario}, {idActual}, {idActual}, 'REPORTE DE FALLA (CLIENTE)', GETDATE(), '{observacionSegura}')", con, trans).ExecuteNonQuery();
                        }
                        trans.Commit();
                        return Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return Json(new { success = false, message = ex.Message });
                    }
                }
            }
        }

        // =======================================================
        // 3. MIS CONTRATOS
        // =======================================================
        public class ContratoClienteItem
        {
            public string IdContratoGenerado { get; set; }
            public string FechaDespacho { get; set; }
            public int EquiposAlquilados { get; set; }
            public decimal ValorTotalHardware { get; set; }
            public int EquiposEnMora { get; set; }
            public decimal MoraTotalAcumulada { get; set; }
        }

        public ActionResult Contratos()
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0) return RedirectToAction("Index", "Login");

            var lista = new List<ContratoClienteItem>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        CAST(r.fecha_ingreso AS DATE) AS fecha_despacho,
                        COUNT(r.id_registro) AS cantidad_equipos,
                        SUM(r.costo_pactado_usd) AS valor_total,
                        SUM(CASE WHEN r.fecha_vencimiento < GETDATE() THEN 1 ELSE 0 END) AS equipos_vencidos,
                        SUM(CASE WHEN r.fecha_vencimiento < GETDATE() THEN DATEDIFF(day, r.fecha_vencimiento, GETDATE()) * ISNULL(r.multa_diaria_usd, 1.00) ELSE 0 END) AS mora_total
                    FROM Registro_Alquiler r
                    INNER JOIN Activos a ON r.id_activo_actual = a.id_activo
                    WHERE a.id_estado = 2 AND r.id_cliente = @id
                    GROUP BY CAST(r.fecha_ingreso AS DATE)
                    ORDER BY fecha_despacho DESC";

                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@id", idCliente);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime fDespacho = Convert.ToDateTime(reader["fecha_despacho"]);
                            lista.Add(new ContratoClienteItem
                            {
                                IdContratoGenerado = "CTR-" + idCliente.ToString("D4") + "-" + fDespacho.ToString("yyMMdd"),
                                FechaDespacho = fDespacho.ToString("dd/MM/yyyy"),
                                EquiposAlquilados = Convert.ToInt32(reader["cantidad_equipos"]),
                                ValorTotalHardware = Convert.ToDecimal(reader["valor_total"]),
                                EquiposEnMora = Convert.ToInt32(reader["equipos_vencidos"]),
                                MoraTotalAcumulada = Convert.ToDecimal(reader["mora_total"])
                            });
                        }
                    }
                }
            }
            return View(lista);
        }

        // =======================================================
        // 4. MIS UBICACIONES Y SEDES (CRUD)
        // =======================================================
        public class SedeClienteItem
        {
            public int IdSede { get; set; }
            public string NombreSede { get; set; }
            public string Departamento { get; set; }
            public string Observacion { get; set; }
            public int TotalAmbientes { get; set; }
        }

        public ActionResult Ubicaciones()
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0) return RedirectToAction("Index", "Login");

            var lista = new List<SedeClienteItem>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        s.id_sede, s.nombre_sede, ISNULL(s.departamento, 'No especificado') AS departamento, 
                        ISNULL(s.observacion, '') AS observacion,
                        (SELECT COUNT(*) FROM Ubicaciones_Internas u WHERE u.id_sede = s.id_sede) AS total_ambientes
                    FROM Sedes s 
                    WHERE s.id_cliente = @idCli
                    ORDER BY s.nombre_sede ASC";

                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@idCli", idCliente);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new SedeClienteItem
                            {
                                IdSede = Convert.ToInt32(reader["id_sede"]),
                                NombreSede = reader["nombre_sede"].ToString(),
                                Departamento = reader["departamento"].ToString(),
                                Observacion = reader["observacion"].ToString(),
                                TotalAmbientes = Convert.ToInt32(reader["total_ambientes"])
                            });
                        }
                    }
                }
            }
            return View(lista);
        }

        [HttpPost]
        public JsonResult GuardarSede(string nombreSede, string departamento, string observacion)
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0) return Json(new { success = false, message = "Sesión inválida." });

            try
            {
                using (var con = new ConexionBD().ObtenerConexion())
                {
                    con.Open();
                    string sql = "INSERT INTO Sedes (id_cliente, nombre_sede, departamento, observacion) VALUES (@idCli, @nom, @dep, @obs)";
                    using (var cmd = new SqlCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@idCli", idCliente);
                        cmd.Parameters.AddWithValue("@nom", nombreSede.Trim());
                        cmd.Parameters.AddWithValue("@dep", string.IsNullOrEmpty(departamento) ? (object)DBNull.Value : departamento.Trim());
                        cmd.Parameters.AddWithValue("@obs", string.IsNullOrEmpty(observacion) ? (object)DBNull.Value : observacion.Trim());
                        cmd.ExecuteNonQuery();
                    }
                }
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // =======================================================
        // 5. MIS AMBIENTES (CRUD POR SEDE)
        // =======================================================
        public class AmbienteClienteItem
        {
            public int IdUbicacion { get; set; }
            public string NombreEspecifico { get; set; }
            public string TipoAmbiente { get; set; }
            public int TotalEquiposFisicos { get; set; }
        }

        public ActionResult Ambientes(int? id)
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0 || id == null) return RedirectToAction("Index", "Login");

            var lista = new List<AmbienteClienteItem>();
            string nombreSede = "";

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                // Validamos que la sede le pertenece a ESTE cliente
                using (var cmd = new SqlCommand("SELECT nombre_sede FROM Sedes WHERE id_sede = @id AND id_cliente = @idCli", con))
                {
                    cmd.Parameters.AddWithValue("@id", id.Value);
                    cmd.Parameters.AddWithValue("@idCli", idCliente);
                    var res = cmd.ExecuteScalar();
                    if (res == null) return RedirectToAction("Ubicaciones");
                    nombreSede = res.ToString();
                }

                string sql = @"
                    SELECT u.id_ubicacion, u.nombre_especifico, ISNULL(u.tipo_ambiente, 'General') AS tipo_ambiente,
                           (SELECT COUNT(*) FROM Activos a WHERE a.id_ubicacion = u.id_ubicacion AND a.id_estado = 2) AS total_equipos
                    FROM Ubicaciones_Internas u 
                    WHERE u.id_sede = @idSede
                    ORDER BY u.nombre_especifico ASC";

                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@idSede", id.Value);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new AmbienteClienteItem
                            {
                                IdUbicacion = Convert.ToInt32(reader["id_ubicacion"]),
                                NombreEspecifico = reader["nombre_especifico"].ToString(),
                                TipoAmbiente = reader["tipo_ambiente"].ToString(),
                                TotalEquiposFisicos = Convert.ToInt32(reader["total_equipos"])
                            });
                        }
                    }
                }
            }

            ViewBag.NombreSede = nombreSede;
            ViewBag.IdSede = id.Value;
            return View(lista);
        }

        [HttpPost]
        public JsonResult GuardarAmbiente(int idSede, string nombreEspecifico, string tipoAmbiente)
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0) return Json(new { success = false });
            try
            {
                using (var con = new ConexionBD().ObtenerConexion())
                {
                    con.Open();
                    string sql = "INSERT INTO Ubicaciones_Internas (id_sede, nombre_especifico, tipo_ambiente) VALUES (@idSede, @nom, @tipo)";
                    using (var cmd = new SqlCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@idSede", idSede);
                        cmd.Parameters.AddWithValue("@nom", nombreEspecifico.Trim());
                        cmd.Parameters.AddWithValue("@tipo", string.IsNullOrEmpty(tipoAmbiente) ? "Oficina/Área" : tipoAmbiente.Trim());
                        cmd.ExecuteNonQuery();
                    }
                }
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // =======================================================
        // 6. DEUDAS Y PAGOS (RENOVACIÓN DE CONTRATO)
        // =======================================================
        public class DeudaLoteItem
        {
            public string LoteContrato { get; set; }
            public string FechaDespacho { get; set; }
            public int EquiposEnMora { get; set; }
            public decimal TotalMora { get; set; }
            public string FechaExactaBD { get; set; }
        }

        public ActionResult Deudas()
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0) return RedirectToAction("Index", "Login");

            var lista = new List<DeudaLoteItem>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        CAST(r.fecha_ingreso AS DATE) AS fecha_despacho,
                        COUNT(r.id_registro) AS equipos_mora,
                        SUM(DATEDIFF(day, r.fecha_vencimiento, GETDATE()) * ISNULL(r.multa_diaria_usd, 1.00)) AS total_mora
                    FROM Registro_Alquiler r
                    INNER JOIN Activos a ON r.id_activo_actual = a.id_activo
                    WHERE r.id_cliente = @id AND a.id_estado = 2 AND r.fecha_vencimiento < GETDATE()
                    GROUP BY CAST(r.fecha_ingreso AS DATE)
                    ORDER BY fecha_despacho DESC";

                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@id", idCliente);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime fecha = Convert.ToDateTime(reader["fecha_despacho"]);
                            lista.Add(new DeudaLoteItem
                            {
                                FechaExactaBD = fecha.ToString("yyyy-MM-dd"),
                                LoteContrato = "CTR-" + idCliente.ToString("D4") + "-" + fecha.ToString("yyMMdd"),
                                FechaDespacho = fecha.ToString("dd/MM/yyyy"),
                                EquiposEnMora = Convert.ToInt32(reader["equipos_mora"]),
                                TotalMora = Convert.ToDecimal(reader["total_mora"])
                            });
                        }
                    }
                }
            }
            return View(lista);
        }

        [HttpPost]
        public JsonResult PagarDeuda(string fechaDespacho)
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0) return Json(new { success = false });

            try
            {
                using (var con = new ConexionBD().ObtenerConexion())
                {
                    con.Open();
                    string sql = @"
                        UPDATE r
                        SET r.fecha_vencimiento = DATEADD(month, 1, GETDATE())
                        FROM Registro_Alquiler r
                        INNER JOIN Activos a ON r.id_activo_actual = a.id_activo
                        WHERE r.id_cliente = @id 
                          AND CAST(r.fecha_ingreso AS DATE) = @fecha 
                          AND r.fecha_vencimiento < GETDATE()
                          AND a.id_estado = 2";

                    using (var cmd = new SqlCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@id", idCliente);
                        cmd.Parameters.AddWithValue("@fecha", fechaDespacho);
                        cmd.ExecuteNonQuery();
                    }
                }
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // =======================================================
        // OTRAS VISTAS DEL CLIENTE
        // =======================================================
        public ActionResult MiPerfil()
        {
            int idCliente = ObtenerIdCliente();
            if (idCliente == 0) return RedirectToAction("Index", "Login");

            string nombreEmpresa = "Empresa no asignada";
            var listaSedes = new List<string>();

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();

                using (var cmd = new SqlCommand("SELECT nombre_empresa FROM Clientes WHERE id_cliente = @id", con))
                {
                    cmd.Parameters.AddWithValue("@id", idCliente);
                    var res = cmd.ExecuteScalar();
                    if (res != DBNull.Value && res != null) nombreEmpresa = res.ToString();
                }

                using (var cmd = new SqlCommand("SELECT nombre_sede FROM Sedes WHERE id_cliente = @id ORDER BY nombre_sede ASC", con))
                {
                    cmd.Parameters.AddWithValue("@id", idCliente);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            listaSedes.Add(reader["nombre_sede"].ToString());
                        }
                    }
                }
            }

            ViewBag.Empresa = nombreEmpresa;
            ViewBag.Sedes = listaSedes;

            return View();
        }

        public ActionResult Facturacion() { return View(); }
        public ActionResult Solicitudes() { return View(); }
        public ActionResult Configuracion() { return View(); }
    }
}