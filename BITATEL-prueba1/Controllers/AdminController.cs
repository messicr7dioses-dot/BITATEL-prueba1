using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using BITATEL_prueba1.Models;

namespace BITATEL_prueba1.Controllers
{
    public class AdminController : Controller
    {
        // =======================================================
        // VALIDACIÓN DE SEGURIDAD CENTRALIZADA
        // =======================================================
        private bool EsAdmin()
        {
            return Session["UsuarioActivo"] is Usuario user && user.IdRol == 1;
        }

        // =======================================================
        // DASHBOARD PRINCIPAL
        // =======================================================
        public ActionResult Index()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            var dashboard = new DashboardAdminViewModel();
            ConexionBD objConexion = new ConexionBD();

            using (var con = objConexion.ObtenerConexion())
            {
                con.Open();

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Activos", con))
                    dashboard.TotalActivos = (int)cmd.ExecuteScalar();

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Activos WHERE id_estado = 1", con))
                    dashboard.ActivosEnStock = (int)cmd.ExecuteScalar();

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Activos WHERE id_estado = 2", con))
                    dashboard.ActivosAlquilados = (int)cmd.ExecuteScalar();

                string sqlContratos = @"SELECT COUNT(*) FROM Registro_Alquiler 
                                        WHERE fecha_vencimiento BETWEEN GETDATE() AND DATEADD(day, 30, GETDATE())";
                using (var cmd = new SqlCommand(sqlContratos, con))
                    dashboard.ContratosPorVencer = (int)cmd.ExecuteScalar();

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Clientes WHERE activo = 1", con))
                    dashboard.ClientesActivos = (int)cmd.ExecuteScalar();

                // NUEVO CÁLCULO DE MORA REAL PARA EL DASHBOARD
                string sqlDeuda = @"SELECT ISNULL(SUM(DATEDIFF(day, r.fecha_vencimiento, GETDATE()) * ISNULL(r.multa_diaria_usd, 1.00)), 0) 
                                    FROM Registro_Alquiler r 
                                    INNER JOIN Activos a ON r.id_activo_actual = a.id_activo 
                                    WHERE a.id_estado = 2 AND r.fecha_vencimiento < GETDATE()";
                using (var cmd = new SqlCommand(sqlDeuda, con))
                    dashboard.TotalDeudaPendiente = Convert.ToDecimal(cmd.ExecuteScalar());
            }

            return View(dashboard);
        }

        // =======================================================
        // LÓGICA DE REGISTRO DE ALQUILER Y DESPACHO
        // =======================================================
        public ActionResult RegistrarAlquiler()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            var clientes = new List<SelectListItem>();
            var categorias = new List<SelectListItem>();
            var ubicaciones = new List<SelectListItem>();

            ConexionBD obj = new ConexionBD();
            using (var con = obj.ObtenerConexion())
            {
                con.Open();

                using (var cmd = new SqlCommand("SELECT id_cliente, nombre_empresa FROM Clientes WHERE activo = 1", con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) clientes.Add(new SelectListItem { Value = reader["id_cliente"].ToString(), Text = reader["nombre_empresa"].ToString() });
                }

                string sqlCat = @"SELECT DISTINCT c.id_categoria, c.nombre_categoria 
                                  FROM Categorias c INNER JOIN Activos a ON c.id_categoria = a.id_categoria 
                                  WHERE a.id_estado = 1";
                using (var cmd = new SqlCommand(sqlCat, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) categorias.Add(new SelectListItem { Value = reader["id_categoria"].ToString(), Text = reader["nombre_categoria"].ToString() });
                }

                string sqlUbi = @"SELECT u.id_ubicacion, s.nombre_sede + ' - ' + u.nombre_especifico AS lugar 
                                  FROM Ubicaciones_Internas u INNER JOIN Sedes s ON u.id_sede = s.id_sede";
                using (var cmd = new SqlCommand(sqlUbi, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) ubicaciones.Add(new SelectListItem { Value = reader["id_ubicacion"].ToString(), Text = reader["lugar"].ToString() });
                }
            }

            ViewBag.ListaClientes = clientes;
            ViewBag.ListaCategorias = categorias;
            ViewBag.ListaUbicaciones = ubicaciones;
            return View();
        }

        [HttpGet]
        public JsonResult ObtenerUbicacionesPorCliente(int idCliente)
        {
            var listaUbicaciones = new List<object>();
            ConexionBD obj = new ConexionBD();

            using (var con = obj.ObtenerConexion())
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
                            listaUbicaciones.Add(new
                            {
                                id = reader["id_ubicacion"].ToString(),
                                texto = reader["lugar"].ToString()
                            });
                        }
                    }
                }
            }
            return Json(listaUbicaciones, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerDetallesCategoria(int idCategoria)
        {
            var marcas = new List<string>();
            decimal costoSugerido = 0;

            ConexionBD obj = new ConexionBD();
            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                using (var cmd = new SqlCommand("SELECT ISNULL(costo_anual_sugerido, 0) FROM Categorias WHERE id_categoria = @id", con))
                {
                    cmd.Parameters.AddWithValue("@id", idCategoria);
                    var result = cmd.ExecuteScalar();
                    if (result != null) costoSugerido = Convert.ToDecimal(result);
                }

                using (var cmd = new SqlCommand("SELECT DISTINCT marca FROM Activos WHERE id_estado = 1 AND id_categoria = @id", con))
                {
                    cmd.Parameters.AddWithValue("@id", idCategoria);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) marcas.Add(reader["marca"].ToString());
                    }
                }
            }
            return Json(new { marcas, costo = costoSugerido }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerActivosFiltrados(int idCategoria, string marca)
        {
            var listaActivos = new List<object>();
            ConexionBD obj = new ConexionBD();
            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                string sql = "SELECT id_activo, etiqueta_activo, modelo FROM Activos WHERE id_estado = 1 AND id_categoria = @idCat AND marca = @marca";
                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@idCat", idCategoria);
                    cmd.Parameters.AddWithValue("@marca", marca);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            listaActivos.Add(new
                            {
                                id = reader["id_activo"].ToString(),
                                texto = reader["etiqueta_activo"].ToString() + " - " + reader["modelo"].ToString()
                            });
                        }
                    }
                }
            }
            return Json(listaActivos, JsonRequestBehavior.AllowGet);
        }

        // <<<<----- AQUÍ APLICAMOS LA MAGIA (ISNULL(c.costo_anual_sugerido, 0)) ----->>>>
        [HttpGet]
        public JsonResult ObtenerActivosPorIds(string ids)
        {
            if (!EsAdmin() || string.IsNullOrEmpty(ids)) return Json(new List<object>(), JsonRequestBehavior.AllowGet);

            var listaActivos = new List<object>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();

                var idsValidos = string.Join(",", ids.Split(',').Where(x => int.TryParse(x, out _)));
                if (string.IsNullOrEmpty(idsValidos)) return Json(listaActivos, JsonRequestBehavior.AllowGet);

                string sql = $@"
                    SELECT a.id_activo, a.etiqueta_activo, c.nombre_categoria, a.marca, a.modelo, ISNULL(c.costo_anual_sugerido, 0) AS costo 
                    FROM Activos a 
                    INNER JOIN Categorias c ON a.id_categoria = c.id_categoria 
                    WHERE a.id_activo IN ({idsValidos}) AND a.id_estado = 1";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        listaActivos.Add(new
                        {
                            idActivo = reader["id_activo"].ToString(),
                            etiqueta = reader["etiqueta_activo"].ToString(),
                            categoria = reader["nombre_categoria"].ToString(),
                            marca = reader["marca"].ToString(),
                            modelo = reader["modelo"].ToString(),
                            costo = Convert.ToDecimal(reader["costo"]) // Envía el costo al frontend
                        });
                    }
                }
            }
            return Json(listaActivos, JsonRequestBehavior.AllowGet);
        }

        // =======================================================
        // GESTIÓN DE CLIENTES
        // =======================================================
        [HttpGet]
        public ActionResult NuevoCliente()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }

        [HttpPost]
        public ActionResult GuardarCliente(string NombreEmpresa, string Ruc, string RazonSocial, string Descripcion, string NombreResponsable, string Username, string Password, string NombreSede, string Departamento, string NombreAmbiente)
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            ConexionBD obj = new ConexionBD();
            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                using (var transaction = con.BeginTransaction())
                {
                    try
                    {
                        string sqlCliente = @"INSERT INTO Clientes (nombre_empresa, ruc, razon_social, descripcion, activo) 
                                              VALUES (@nombre, @ruc, @razon, @desc, 1);
                                              SELECT SCOPE_IDENTITY();";
                        long nuevoIdCliente = 0;

                        using (var cmdCli = new SqlCommand(sqlCliente, con, transaction))
                        {
                            cmdCli.Parameters.AddWithValue("@nombre", NombreEmpresa.Trim());
                            cmdCli.Parameters.AddWithValue("@ruc", Ruc.Trim());
                            cmdCli.Parameters.AddWithValue("@razon", RazonSocial.Trim());
                            cmdCli.Parameters.AddWithValue("@desc", string.IsNullOrEmpty(Descripcion) ? (object)DBNull.Value : Descripcion);

                            var resultId = cmdCli.ExecuteScalar();
                            if (resultId == null || resultId == DBNull.Value)
                                throw new Exception("Error crítico: La base de datos no devolvió el ID del nuevo cliente.");

                            nuevoIdCliente = Convert.ToInt64(resultId);
                        }

                        string sqlUsuario = @"INSERT INTO Usuarios (nombre_completo, username, password_hash, id_rol, id_cliente, activo) 
                                              VALUES (@nombreComp, @user, @pass, 2, @idCli, 1)";
                        using (var cmdUsu = new SqlCommand(sqlUsuario, con, transaction))
                        {
                            cmdUsu.Parameters.AddWithValue("@nombreComp", NombreResponsable.Trim());
                            cmdUsu.Parameters.AddWithValue("@user", Username.ToLower().Trim());
                            cmdUsu.Parameters.AddWithValue("@pass", Password.Trim());
                            cmdUsu.Parameters.AddWithValue("@idCli", nuevoIdCliente);
                            cmdUsu.ExecuteNonQuery();
                        }

                        if (!string.IsNullOrEmpty(NombreSede))
                        {
                            string sqlSede = @"INSERT INTO Sedes (nombre_sede, departamento, id_cliente) 
                                               VALUES (@nomSede, @dep, @idCli);
                                               SELECT SCOPE_IDENTITY();";
                            long nuevoIdSede = 0;
                            using (var cmdSede = new SqlCommand(sqlSede, con, transaction))
                            {
                                cmdSede.Parameters.AddWithValue("@nomSede", NombreSede.Trim());
                                cmdSede.Parameters.AddWithValue("@dep", string.IsNullOrEmpty(Departamento) ? "Desconocido" : Departamento.Trim());
                                cmdSede.Parameters.AddWithValue("@idCli", nuevoIdCliente);
                                nuevoIdSede = Convert.ToInt64(cmdSede.ExecuteScalar());
                            }

                            string sqlUbi = @"INSERT INTO Ubicaciones_Internas (id_sede, nombre_especifico, tipo_ambiente) 
                                              VALUES (@idSede, @nomAmb, 'Oficina/Área')";
                            using (var cmdUbi = new SqlCommand(sqlUbi, con, transaction))
                            {
                                cmdUbi.Parameters.AddWithValue("@idSede", nuevoIdSede);
                                cmdUbi.Parameters.AddWithValue("@nomAmb", string.IsNullOrEmpty(NombreAmbiente) ? "Principal" : NombreAmbiente.Trim());
                                cmdUbi.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                        return RedirectToAction("Index", "Admin");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return Content("<div style='padding:40px; font-family:sans-serif;'>" +
                                       "<h2 style='color:#dc2626;'>ERROR SQL DETECTADO:</h2>" +
                                       "<p style='font-size:1.1rem;'>" + ex.Message + "</p>" +
                                       "<a href='javascript:history.back()' style='display:inline-block; margin-top:20px; padding:10px 20px; background:#2563eb; color:white; text-decoration:none; border-radius:5px;'>Volver al formulario</a>" +
                                       "</div>");
                    }
                }
            }
        }

        // =======================================================
        // CLASES AUXILIARES DE MAPEO JSON
        // =======================================================
        public class SolicitudItemLote
        {
            public int IdActivo { get; set; }
            public decimal CostoPactado { get; set; }
            public decimal MultaDiaria { get; set; }
        }

        public class PayloadLoteAlquiler
        {
            public int IdCliente { get; set; }
            public int IdUbicacionDestino { get; set; }
            public string FechaIngreso { get; set; }
            public string FechaVencimiento { get; set; }
            public List<SolicitudItemLote> Items { get; set; }
        }

        // =======================================================
        // EL MÉTOD CORE DE NEGOCIO EN LOTE (SqlTransaction Completo)
        // =======================================================
        [HttpPost]
        public ActionResult GuardarAlquilerMasivo(PayloadLoteAlquiler modelo)
        {
            if (!(Session["UsuarioActivo"] is Usuario user && user.IdRol == 1))
                return new HttpStatusCodeResult(401);

            if (modelo == null || modelo.Items == null || modelo.Items.Count == 0)
                return new HttpStatusCodeResult(400);

            ConexionBD obj = new ConexionBD();
            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                using (var transaction = con.BeginTransaction())
                {
                    try
                    {
                        string prefijoCliente = "XXX";
                        string sqlPref = "SELECT TOP 1 username FROM Usuarios WHERE id_cliente = @id AND id_rol = 2";
                        using (var cmdPref = new SqlCommand(sqlPref, con, transaction))
                        {
                            cmdPref.Parameters.AddWithValue("@id", modelo.IdCliente);
                            var resultUser = cmdPref.ExecuteScalar();
                            if (resultUser != null)
                            {
                                string userCorto = resultUser.ToString().Trim().ToUpper();
                                prefijoCliente = userCorto.Length >= 3 ? userCorto.Substring(0, 3) : userCorto.PadRight(3, 'X');
                            }
                        }

                        foreach (var item in modelo.Items)
                        {
                            string etiquetaActual = "";
                            int idUbicacionOrigen = 0;
                            using (var cmdData = new SqlCommand("SELECT etiqueta_activo, id_ubicacion FROM Activos WHERE id_activo = @id", con, transaction))
                            {
                                cmdData.Parameters.AddWithValue("@id", item.IdActivo);
                                using (var reader = cmdData.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        etiquetaActual = reader["etiqueta_activo"].ToString();
                                        idUbicacionOrigen = reader["id_ubicacion"] != DBNull.Value ? Convert.ToInt32(reader["id_ubicacion"]) : 0;
                                    }
                                }
                            }

                            string[] partesEtiqueta = etiquetaActual.Split('-');
                            string codigoCategoria = partesEtiqueta.Length > 1 ? partesEtiqueta[1] : "GEN";

                            int nuevoCorrelativo = 1;
                            string sqlMax = "SELECT MAX(CAST(RIGHT(etiqueta_activo, 6) AS INT)) FROM Activos WHERE etiqueta_activo LIKE @patron";
                            using (var cmdMax = new SqlCommand(sqlMax, con, transaction))
                            {
                                cmdMax.Parameters.AddWithValue("@patron", prefijoCliente + "-" + codigoCategoria + "-%");
                                var resultMax = cmdMax.ExecuteScalar();
                                if (resultMax != DBNull.Value && resultMax != null)
                                {
                                    nuevoCorrelativo = Convert.ToInt32(resultMax) + 1;
                                }
                            }

                            string nuevaEtiqueta = $"{prefijoCliente}-{codigoCategoria}-{nuevoCorrelativo:D6}";

                            string updateActivo = "UPDATE Activos SET etiqueta_activo = @newEtiqueta, id_estado = 2, id_ubicacion = @idUbi WHERE id_activo = @idActivo";
                            using (var cmdA = new SqlCommand(updateActivo, con, transaction))
                            {
                                cmdA.Parameters.AddWithValue("@newEtiqueta", nuevaEtiqueta);
                                cmdA.Parameters.AddWithValue("@idUbi", modelo.IdUbicacionDestino);
                                cmdA.Parameters.AddWithValue("@idActivo", item.IdActivo);
                                cmdA.ExecuteNonQuery();
                            }

                            string insertAlquiler = @"INSERT INTO Registro_Alquiler 
                                                    (id_activo_actual, id_activo_original, id_cliente, id_usuario_registro, fecha_ingreso, fecha_vencimiento, costo_pactado_usd, multa_diaria_usd) 
                                                    VALUES (@idActivo, @idActivo, @idCli, @idUsu, @ingreso, @vence, @costo, @multa)";
                            using (var cmdReg = new SqlCommand(insertAlquiler, con, transaction))
                            {
                                cmdReg.Parameters.AddWithValue("@idActivo", item.IdActivo);
                                cmdReg.Parameters.AddWithValue("@idCli", modelo.IdCliente);
                                cmdReg.Parameters.AddWithValue("@idUsu", user.IdUsuario);
                                cmdReg.Parameters.AddWithValue("@ingreso", Convert.ToDateTime(modelo.FechaIngreso));
                                cmdReg.Parameters.AddWithValue("@vence", Convert.ToDateTime(modelo.FechaVencimiento));
                                cmdReg.Parameters.AddWithValue("@costo", item.CostoPactado);
                                cmdReg.Parameters.AddWithValue("@multa", item.MultaDiaria);
                                cmdReg.ExecuteNonQuery();
                            }

                            string insertMov = @"INSERT INTO Movimientos 
                                               (id_activo, id_usuario, id_ubicacion_origen, id_ubicacion_destino, motivo, fecha_movimiento) 
                                               VALUES (@idActivo, @idUsu, @origen, @destino, 'NUEVO CONTRATO', GETDATE())";
                            using (var cmdMov = new SqlCommand(insertMov, con, transaction))
                            {
                                cmdMov.Parameters.AddWithValue("@idActivo", item.IdActivo);
                                cmdMov.Parameters.AddWithValue("@idUsu", user.IdUsuario);
                                cmdMov.Parameters.AddWithValue("@origen", idUbicacionOrigen == 0 ? (object)DBNull.Value : idUbicacionOrigen);
                                cmdMov.Parameters.AddWithValue("@destino", modelo.IdUbicacionDestino);
                                cmdMov.ExecuteNonQuery();
                            }

                            string insertAud = @"INSERT INTO Auditoria_Sistema 
                                               (id_usuario, tabla_afectada, campo_afectado, accion, fecha_hora, valor_anterior, valor_nuevo) 
                                               VALUES (@idUsu, 'Activos', 'etiqueta_activo', 'DESPACHO LOTE MASIVO', GETDATE(), @oldEtiq, @newEtiq)";
                            using (var cmdAud = new SqlCommand(insertAud, con, transaction))
                            {
                                cmdAud.Parameters.AddWithValue("@idUsu", user.IdUsuario);
                                cmdAud.Parameters.AddWithValue("@oldEtiq", etiquetaActual);
                                cmdAud.Parameters.AddWithValue("@newEtiq", nuevaEtiqueta);
                                cmdAud.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                        return Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return Json(new { success = false, error = ex.Message });
                    }
                }
            }
        }

        // =======================================================
        // MÓDULOS DE AUDITORÍA (Movimientos y Garantías)
        // =======================================================
        public class MovimientoItem
        {
            public string Fecha { get; set; }
            public string Cliente { get; set; }
            public string Etiqueta { get; set; }
            public string Equipo { get; set; }
            public string Origen { get; set; }
            public string Destino { get; set; }
            public string Motivo { get; set; }
            public string Observacion { get; set; }
            public string Usuario { get; set; }
        }

        public ActionResult Movimientos()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            var lista = new List<MovimientoItem>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        m.fecha_movimiento, 
                        ISNULL(a.etiqueta_activo, 'Sin Etiqueta') AS etiqueta_activo, 
                        ISNULL(cat.nombre_categoria, '') + ' - ' + ISNULL(a.marca, '') + ' ' + ISNULL(a.modelo, '') AS equipo, 
                        ISNULL(uo.nombre_especifico, 'Almacén Local / Stock') AS origen, 
                        ISNULL(ud.nombre_especifico, 'Destino Desconocido') AS destino, 
                        ISNULL(m.motivo, 'Movimiento') AS motivo, 
                        ISNULL(m.observacion, '') AS observacion,
                        ISNULL(us.nombre_completo, 'Sistema') AS usuario,
                        ISNULL(c.nombre_empresa, 'BITATEL (Interno)') AS cliente
                    FROM Movimientos m
                    LEFT JOIN Activos a ON m.id_activo = a.id_activo
                    LEFT JOIN Categorias cat ON a.id_categoria = cat.id_categoria
                    LEFT JOIN Ubicaciones_Internas uo ON m.id_ubicacion_origen = uo.id_ubicacion
                    LEFT JOIN Ubicaciones_Internas ud ON m.id_ubicacion_destino = ud.id_ubicacion
                    LEFT JOIN Sedes s ON ud.id_sede = s.id_sede
                    LEFT JOIN Clientes c ON s.id_cliente = c.id_cliente
                    LEFT JOIN Usuarios us ON m.id_usuario = us.id_usuario
                    WHERE m.motivo NOT LIKE '%GARANTÍA%'
                    ORDER BY m.fecha_movimiento DESC";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new MovimientoItem
                        {
                            Fecha = reader["fecha_movimiento"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_movimiento"]).ToString("dd/MM/yyyy HH:mm") : "-",
                            Cliente = reader["cliente"].ToString(),
                            Etiqueta = reader["etiqueta_activo"].ToString(),
                            Equipo = reader["equipo"].ToString(),
                            Origen = reader["origen"].ToString(),
                            Destino = reader["destino"].ToString(),
                            Motivo = reader["motivo"].ToString(),
                            Observacion = reader["observacion"].ToString(),
                            Usuario = reader["usuario"].ToString()
                        });
                    }
                }
            }
            ViewBag.Titulo = "Auditoría de Movimientos Físicos";
            ViewBag.EsGarantia = false;
            return View(lista);
        }

        public ActionResult Garantias()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            var lista = new List<MovimientoItem>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        m.fecha_movimiento, 
                        ISNULL(a.etiqueta_activo, 'Sin Etiqueta') AS etiqueta_activo, 
                        ISNULL(cat.nombre_categoria, '') + ' - ' + ISNULL(a.marca, '') + ' ' + ISNULL(a.modelo, '') AS equipo, 
                        ISNULL(uo.nombre_especifico, 'Almacén Local') AS origen, 
                        ISNULL(m.motivo, 'Garantía') AS motivo, 
                        ISNULL(m.observacion, 'Sin detalles') AS observacion,
                        ISNULL(us.nombre_completo, 'Sistema') AS usuario,
                        ISNULL(c.nombre_empresa, 'BITATEL (Interno)') AS cliente
                    FROM Movimientos m
                    LEFT JOIN Activos a ON m.id_activo = a.id_activo
                    LEFT JOIN Categorias cat ON a.id_categoria = cat.id_categoria
                    LEFT JOIN Ubicaciones_Internas uo ON m.id_ubicacion_origen = uo.id_ubicacion
                    LEFT JOIN Sedes s ON uo.id_sede = s.id_sede
                    LEFT JOIN Clientes c ON s.id_cliente = c.id_cliente
                    LEFT JOIN Usuarios us ON m.id_usuario = us.id_usuario
                    WHERE m.motivo LIKE '%GARANTÍA%'
                    ORDER BY m.fecha_movimiento DESC";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new MovimientoItem
                        {
                            Fecha = reader["fecha_movimiento"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_movimiento"]).ToString("dd/MM/yyyy HH:mm") : "-",
                            Cliente = reader["cliente"].ToString(),
                            Etiqueta = reader["etiqueta_activo"].ToString(),
                            Equipo = reader["equipo"].ToString(),
                            Origen = reader["origen"].ToString(),
                            Destino = "Laboratorio de Revisión (RMA)",
                            Motivo = reader["motivo"].ToString(),
                            Observacion = reader["observacion"].ToString(),
                            Usuario = reader["usuario"].ToString()
                        });
                    }
                }
            }
            ViewBag.Titulo = "Historial y Trazabilidad de Garantías (RMA)";
            ViewBag.EsGarantia = true;
            return View("Movimientos", lista);
        }

        // =======================================================
        // MÓDULOS DE INVENTARIO Y STOCK (RUTAS DEL DASHBOARD)
        // =======================================================
        public class ClienteStockItem
        {
            public int IdCliente { get; set; }
            public string NombreEmpresa { get; set; }
            public int EquiposActivos { get; set; }
        }

        public class StockLocalItem
        {
            public int IdActivo { get; set; }
            public string Etiqueta { get; set; }
            public string Componente { get; set; }
            public string Marca { get; set; }
            public string Modelo { get; set; }
            public string Serie { get; set; }
            public string Ubicacion { get; set; }
            public string EstadoFisico { get; set; }
        }

        public ActionResult GestionarStock(string filtro = "todo")
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            var lista = new List<ClienteStockItem>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        c.id_cliente, 
                        c.nombre_empresa, 
                        (SELECT COUNT(*) FROM Registro_Alquiler r 
                         INNER JOIN Activos a ON r.id_activo_actual = a.id_activo 
                         WHERE r.id_cliente = c.id_cliente AND a.id_estado = 2) AS total_equipos
                    FROM Clientes c
                    WHERE c.activo = 1";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ClienteStockItem
                        {
                            IdCliente = Convert.ToInt32(reader["id_cliente"]),
                            NombreEmpresa = reader["nombre_empresa"].ToString(),
                            EquiposActivos = Convert.ToInt32(reader["total_equipos"])
                        });
                    }
                }

                using (var cmdStock = new SqlCommand("SELECT COUNT(*) FROM Activos WHERE id_estado = 1", con))
                {
                    ViewBag.TotalStock = (int)cmdStock.ExecuteScalar();
                }
            }

            ViewBag.Filtro = filtro;
            return View(lista);
        }

        public ActionResult StockLocal()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            var lista = new List<StockLocalItem>();

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        a.id_activo,
                        a.etiqueta_activo, 
                        cat.nombre_categoria AS componente, 
                        a.marca,
                        a.modelo,
                        a.serie, 
                        ISNULL(s.nombre_sede + ' - ' + u.nombre_especifico, 'Almacén Principal') AS ubicacion,
                        e.nombre_estado AS estado_fisico
                    FROM Activos a
                    INNER JOIN Categorias cat ON a.id_categoria = cat.id_categoria
                    INNER JOIN Estados_Activo e ON a.id_estado = e.id_estado
                    LEFT JOIN Ubicaciones_Internas u ON a.id_ubicacion = u.id_ubicacion
                    LEFT JOIN Sedes s ON u.id_sede = s.id_sede
                    WHERE a.id_estado = 1";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new StockLocalItem
                        {
                            IdActivo = Convert.ToInt32(reader["id_activo"]),
                            Etiqueta = reader["etiqueta_activo"].ToString(),
                            Componente = reader["componente"].ToString(),
                            Marca = reader["marca"].ToString(),
                            Modelo = reader["modelo"].ToString(),
                            Serie = reader["serie"].ToString(),
                            Ubicacion = reader["ubicacion"].ToString(),
                            EstadoFisico = reader["estado_fisico"].ToString()
                        });
                    }
                }
            }
            return View(lista);
        }

        // =======================================================
        // VISTA DETALLADA DEL INVENTARIO DEL CLIENTE (ACTION HUB)
        // =======================================================
        public class InventarioClienteItem
        {
            public int IdActivo { get; set; }
            public string Etiqueta { get; set; }
            public string Componente { get; set; }
            public string Marca { get; set; }
            public string Modelo { get; set; }
            public string Serie { get; set; }
            public string Sede { get; set; }
            public string Ubicacion { get; set; }
            public string EstadoFisico { get; set; }
            public string FechaDespacho { get; set; }
            public string FechaDevolucion { get; set; }
            public decimal CostoProducto { get; set; }
            public decimal CuotaMensual { get; set; }
            public string EstadoMora { get; set; }
            public string Contrato { get; set; }
            public bool EstaVencido { get; set; }

            // ¡NUEVOS CAMPOS FINANCIEROS PARA EL DESGLOSE!
            public decimal MultaDiaria { get; set; }
            public int DiasMora { get; set; }
            public decimal TotalMora { get; set; }
        }

        public ActionResult InventarioCliente(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            if (id == null) return RedirectToAction("Clientes");

            var lista = new List<InventarioClienteItem>();
            string nombreCliente = "";

            ConexionBD obj = new ConexionBD();
            using (var con = obj.ObtenerConexion())
            {
                con.Open();

                using (var cmdCli = new SqlCommand("SELECT nombre_empresa FROM Clientes WHERE id_cliente = @id", con))
                {
                    cmdCli.Parameters.AddWithValue("@id", id.Value);
                    nombreCliente = cmdCli.ExecuteScalar()?.ToString();
                }

                // MAGIA AQUÍ: a.id_estado IN (2, 3) permite ver Producción y Averiados
                string sql = @"
                    SELECT 
                        a.id_activo, a.etiqueta_activo, cat.nombre_categoria AS componente, a.marca, a.modelo, a.serie, 
                        s.nombre_sede AS sede, u.nombre_especifico AS ubicacion, e.nombre_estado AS estado_fisico,
                        r.fecha_ingreso, r.fecha_vencimiento, r.costo_pactado_usd, 
                        ISNULL(r.multa_diaria_usd, 1.00) AS multa_diaria_usd, r.id_registro
                    FROM Registro_Alquiler r
                    INNER JOIN Activos a ON r.id_activo_actual = a.id_activo
                    INNER JOIN Categorias cat ON a.id_categoria = cat.id_categoria
                    INNER JOIN Ubicaciones_Internas u ON a.id_ubicacion = u.id_ubicacion
                    INNER JOIN Sedes s ON u.id_sede = s.id_sede
                    INNER JOIN Estados_Activo e ON a.id_estado = e.id_estado
                    WHERE r.id_cliente = @idCliente AND a.id_estado IN (2, 3)";

                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@idCliente", id.Value);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime ingreso = Convert.ToDateTime(reader["fecha_ingreso"]);
                            DateTime vencimiento = Convert.ToDateTime(reader["fecha_vencimiento"]);
                            decimal costoTotal = Convert.ToDecimal(reader["costo_pactado_usd"]);
                            decimal multaDiaria = Convert.ToDecimal(reader["multa_diaria_usd"]);

                            int meses = ((vencimiento.Year - ingreso.Year) * 12) + vencimiento.Month - ingreso.Month;
                            if (meses <= 0) meses = 1;

                            bool caducado = vencimiento < DateTime.Now;
                            int diasRetraso = 0;
                            decimal totalDeudaMora = 0;

                            if (caducado)
                            {
                                diasRetraso = (DateTime.Now - vencimiento).Days;
                                if (diasRetraso < 0) diasRetraso = 0;
                                totalDeudaMora = diasRetraso * multaDiaria;
                            }

                            string idGenerado = "CTR-" + id.Value.ToString("D4") + "-" + ingreso.ToString("yyMMdd");

                            lista.Add(new InventarioClienteItem
                            {
                                IdActivo = Convert.ToInt32(reader["id_activo"]),
                                Etiqueta = reader["etiqueta_activo"].ToString(),
                                Componente = reader["componente"].ToString(),
                                Marca = reader["marca"].ToString(),
                                Modelo = reader["modelo"].ToString(),
                                Serie = reader["serie"].ToString(),
                                Sede = reader["sede"].ToString(),
                                Ubicacion = reader["ubicacion"].ToString(),
                                EstadoFisico = reader["estado_fisico"].ToString(),
                                FechaDespacho = ingreso.ToString("dd/MM/yyyy"),
                                FechaDevolucion = vencimiento.ToString("dd/MM/yyyy"),
                                CostoProducto = costoTotal,
                                CuotaMensual = Math.Round(costoTotal / meses, 2),
                                EstadoMora = caducado ? "VENCIDO" : "AL DÍA",
                                Contrato = idGenerado,
                                EstaVencido = caducado,
                                MultaDiaria = multaDiaria,
                                DiasMora = diasRetraso,
                                TotalMora = totalDeudaMora
                            });
                        }
                    }
                }
            }

            ViewBag.NombreCliente = nombreCliente;
            ViewBag.IdCliente = id.Value;
            return View(lista);
        }

        // =======================================================
        // ACCIONES DE LOTE: TRASLADO Y GARANTÍA (SWAP)
        // =======================================================
        [HttpPost]
        public JsonResult EjecutarTraslado(List<int> ids, int idUbicacionDestino)
        {
            if (!EsAdmin() || ids == null || ids.Count == 0) return Json(new { success = false, message = "Datos inválidos." });
            Usuario user = (Usuario)Session["UsuarioActivo"];

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                using (var trans = con.BeginTransaction())
                {
                    try
                    {
                        foreach (int idActivo in ids)
                        {
                            int idOrigen = 0;
                            using (var cmdUbi = new SqlCommand($"SELECT id_ubicacion FROM Activos WHERE id_activo = {idActivo}", con, trans))
                            {
                                var res = cmdUbi.ExecuteScalar();
                                if (res != DBNull.Value && res != null) idOrigen = Convert.ToInt32(res);
                            }

                            new SqlCommand($"UPDATE Activos SET id_ubicacion = {idUbicacionDestino} WHERE id_activo = {idActivo}", con, trans).ExecuteNonQuery();
                            new SqlCommand($"INSERT INTO Movimientos (id_activo, id_usuario, id_ubicacion_origen, id_ubicacion_destino, motivo, fecha_movimiento) VALUES ({idActivo}, {user.IdUsuario}, {idOrigen}, {idUbicacionDestino}, 'TRASLADO INTERNO', GETDATE())", con, trans).ExecuteNonQuery();
                        }
                        trans.Commit();
                        return Json(new { success = true });
                    }
                    catch (Exception ex) { trans.Rollback(); return Json(new { success = false, message = ex.Message }); }
                }
            }
        }

        [HttpGet]
        public JsonResult ObtenerStockParaReemplazo(int idActivoMalo)
        {
            if (!EsAdmin()) return Json(new { error = "No autorizado" }, JsonRequestBehavior.AllowGet);

            var lista = new List<object>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    DECLARE @idCat INT = (SELECT id_categoria FROM Activos WHERE id_activo = @idMalo);
                    SELECT id_activo, etiqueta_activo, marca, modelo 
                    FROM Activos 
                    WHERE id_estado = 1 AND id_categoria = @idCat";

                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@idMalo", idActivoMalo);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new
                            {
                                id = reader["id_activo"].ToString(),
                                texto = $"{reader["etiqueta_activo"]} - {reader["marca"]} {reader["modelo"]}"
                            });
                        }
                    }
                }
            }
            return Json(lista, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult EjecutarGarantia(int idActivoViejo, int idActivoNuevo, string motivo)
        {
            if (!EsAdmin() || idActivoViejo == 0 || idActivoNuevo == 0) return Json(new { success = false, message = "Datos inválidos." });
            Usuario user = (Usuario)Session["UsuarioActivo"];

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                using (var trans = con.BeginTransaction())
                {
                    try
                    {
                        int idUbicacionCliente = 0;
                        string etiquetaNuevo = "";

                        using (var cmdUbi = new SqlCommand($"SELECT id_ubicacion FROM Activos WHERE id_activo = {idActivoViejo}", con, trans))
                        {
                            var res = cmdUbi.ExecuteScalar();
                            if (res != DBNull.Value && res != null) idUbicacionCliente = Convert.ToInt32(res);
                        }

                        using (var cmdEtiq = new SqlCommand($"SELECT etiqueta_activo FROM Activos WHERE id_activo = {idActivoNuevo}", con, trans))
                        {
                            etiquetaNuevo = cmdEtiq.ExecuteScalar()?.ToString();
                        }

                        new SqlCommand($"UPDATE Activos SET id_estado = 3 WHERE id_activo = {idActivoViejo}", con, trans).ExecuteNonQuery();
                        new SqlCommand($"UPDATE Activos SET id_estado = 2, id_ubicacion = {idUbicacionCliente} WHERE id_activo = {idActivoNuevo}", con, trans).ExecuteNonQuery();
                        new SqlCommand($"UPDATE Registro_Alquiler SET id_activo_actual = {idActivoNuevo} WHERE id_activo_actual = {idActivoViejo}", con, trans).ExecuteNonQuery();

                        string observacionCompleta = $"{motivo.Replace("'", "''")} | REEMPLAZO: {etiquetaNuevo}";
                        new SqlCommand($"INSERT INTO Movimientos (id_activo, id_usuario, id_ubicacion_origen, id_ubicacion_destino, motivo, fecha_movimiento, observacion) VALUES ({idActivoViejo}, {user.IdUsuario}, {idUbicacionCliente}, {idUbicacionCliente}, 'GARANTÍA / RMA', GETDATE(), '{observacionCompleta}')", con, trans).ExecuteNonQuery();

                        trans.Commit();
                        return Json(new { success = true });
                    }
                    catch (Exception ex) { trans.Rollback(); return Json(new { success = false, message = ex.Message }); }
                }
            }
        }

        // =======================================================
        // MÓDULO DE GESTIÓN DE USUARIOS
        // =======================================================
        public class UsuarioItem
        {
            public long IdUsuario { get; set; }
            public string NombreCompleto { get; set; }
            public string Username { get; set; }
            public string Rol { get; set; }
            public string Empresa { get; set; }
            public bool IsActivo { get; set; }
        }

        public ActionResult Usuarios()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            var lista = new List<UsuarioItem>();
            ConexionBD obj = new ConexionBD();

            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        u.id_usuario, 
                        u.nombre_completo, 
                        u.username, 
                        r.nombre_rol, 
                        ISNULL(c.nombre_empresa, 'Personal Interno BITATEL') AS empresa,
                        u.activo
                    FROM Usuarios u
                    INNER JOIN Roles r ON u.id_rol = r.id_rol
                    LEFT JOIN Clientes c ON u.id_cliente = c.id_cliente
                    ORDER BY u.id_rol ASC, u.nombre_completo ASC";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new UsuarioItem
                        {
                            IdUsuario = Convert.ToInt64(reader["id_usuario"]),
                            NombreCompleto = reader["nombre_completo"].ToString(),
                            Username = reader["username"].ToString(),
                            Rol = reader["nombre_rol"].ToString(),
                            Empresa = reader["empresa"].ToString(),
                            IsActivo = Convert.ToBoolean(reader["activo"])
                        });
                    }
                }
            }
            return View(lista);
        }

        [HttpPost]
        public ActionResult CambiarEstadoUsuario(long id)
        {
            if (!EsAdmin()) return new HttpStatusCodeResult(401);

            Usuario userLogueado = (Usuario)Session["UsuarioActivo"];
            if (userLogueado.IdUsuario == id)
            {
                return Json(new { success = false, message = "No puedes desactivar tu propia cuenta." });
            }

            ConexionBD obj = new ConexionBD();
            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                string sql = "UPDATE Usuarios SET activo = ~activo WHERE id_usuario = @id";
                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            return Json(new { success = true });
        }

        // =======================================================
        // CREACIÓN DE NUEVOS USUARIOS
        // =======================================================
        [HttpGet]
        public ActionResult NuevoUsuario()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            var clientes = new List<SelectListItem>();
            var roles = new List<SelectListItem>();
            ConexionBD obj = new ConexionBD();

            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                using (var cmd = new SqlCommand("SELECT id_cliente, nombre_empresa FROM Clientes WHERE activo = 1", con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) clientes.Add(new SelectListItem { Value = reader["id_cliente"].ToString(), Text = reader["nombre_empresa"].ToString() });
                }

                using (var cmd = new SqlCommand("SELECT id_rol, nombre_rol FROM Roles", con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) roles.Add(new SelectListItem { Value = reader["id_rol"].ToString(), Text = reader["nombre_rol"].ToString() });
                }
            }

            ViewBag.ListaClientes = clientes;
            ViewBag.ListaRoles = roles;
            return View();
        }

        [HttpPost]
        public ActionResult GuardarUsuario(string NombreCompleto, string Username, string Password, int IdRol, int? IdCliente)
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            ConexionBD obj = new ConexionBD();
            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                if (IdRol == 2 && (IdCliente == null || IdCliente == 0))
                {
                    return Content("<h2 style='color:red;'>Error de Integridad:</h2><p>Un usuario de rol Cliente no puede ser creado sin una empresa asignada.</p><a href='javascript:history.back()'>Volver</a>");
                }

                object clienteFinal = (IdRol == 2 && IdCliente.HasValue) ? (object)IdCliente.Value : DBNull.Value;

                string sql = @"INSERT INTO Usuarios (nombre_completo, username, password_hash, id_rol, id_cliente, activo) 
                               VALUES (@nombre, @user, @pass, @rol, @idCli, 1)";
                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@nombre", NombreCompleto.Trim());
                    cmd.Parameters.AddWithValue("@user", Username.ToLower().Trim());
                    cmd.Parameters.AddWithValue("@pass", Password.Trim());
                    cmd.Parameters.AddWithValue("@rol", IdRol);
                    cmd.Parameters.AddWithValue("@idCli", clienteFinal);
                    cmd.ExecuteNonQuery();
                }
            }
            return RedirectToAction("Usuarios", "Admin");
        }

        // =======================================================
        // MÓDULO DE CLIENTES (Directorio)
        // =======================================================
        public class DirectorioClienteItem
        {
            public int IdCliente { get; set; }
            public string NombreEmpresa { get; set; }
            public string Ruc { get; set; }
            public int CantidadContratos { get; set; }
            public int CantidadUbicaciones { get; set; }
        }

        public ActionResult Clientes()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            var lista = new List<DirectorioClienteItem>();
            ConexionBD obj = new ConexionBD();

            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        c.id_cliente, 
                        c.nombre_empresa,
                        c.ruc,
                        (SELECT COUNT(DISTINCT CAST(r.fecha_ingreso AS DATE)) 
                         FROM Registro_Alquiler r 
                         WHERE r.id_cliente = c.id_cliente AND r.fecha_vencimiento >= GETDATE()) as contratos_activos,
                        (SELECT COUNT(*) FROM Sedes s WHERE s.id_cliente = c.id_cliente) as total_sedes
                    FROM Clientes c 
                    WHERE c.activo = 1";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new DirectorioClienteItem
                        {
                            IdCliente = Convert.ToInt32(reader["id_cliente"]),
                            NombreEmpresa = reader["nombre_empresa"].ToString(),
                            Ruc = reader["ruc"].ToString(),
                            CantidadContratos = Convert.ToInt32(reader["contratos_activos"]),
                            CantidadUbicaciones = Convert.ToInt32(reader["total_sedes"])
                        });
                    }
                }
            }
            return View(lista);
        }

        // =======================================================
        // MÓDULO DE CONTRATOS (SEPARADOS POR LOTE/FECHA)
        // =======================================================
        public class ContratoMaestroItem
        {
            public int IdCliente { get; set; }
            public string Empresa { get; set; }
            public string Ruc { get; set; }
            public string FechaDespacho { get; set; }
            public string IdContratoGenerado { get; set; }
            public int EquiposAlquilados { get; set; }
            public decimal ValorTotalHardware { get; set; }
            public string ProximoVencimiento { get; set; }
            public int EquiposEnMora { get; set; }
            public decimal MoraTotalAcumulada { get; set; } // ¡NUEVO CAMPO FINANCIERO!
        }

        public ActionResult Contratos()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            var lista = new List<ContratoMaestroItem>();
            ConexionBD obj = new ConexionBD();

            using (var con = obj.ObtenerConexion())
            {
                con.Open();
                // Ahora la consulta SQL calcula la suma de (Días de retraso * Multa diaria)
                string sql = @"
                    SELECT 
                        c.id_cliente,
                        c.nombre_empresa,
                        c.ruc,
                        CAST(r.fecha_ingreso AS DATE) AS fecha_despacho,
                        COUNT(r.id_registro) AS cantidad_equipos,
                        SUM(r.costo_pactado_usd) AS valor_total,
                        MIN(r.fecha_vencimiento) AS proximo_vence,
                        SUM(CASE WHEN r.fecha_vencimiento < GETDATE() THEN 1 ELSE 0 END) AS equipos_vencidos,
                        SUM(CASE WHEN r.fecha_vencimiento < GETDATE() THEN DATEDIFF(day, r.fecha_vencimiento, GETDATE()) * ISNULL(r.multa_diaria_usd, 1.00) ELSE 0 END) AS mora_total
                    FROM Clientes c
                    INNER JOIN Registro_Alquiler r ON c.id_cliente = r.id_cliente
                    INNER JOIN Activos a ON r.id_activo_actual = a.id_activo
                    WHERE a.id_estado = 2 AND c.activo = 1
                    GROUP BY c.id_cliente, c.nombre_empresa, c.ruc, CAST(r.fecha_ingreso AS DATE)
                    ORDER BY fecha_despacho DESC, equipos_vencidos DESC";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime minVenc = Convert.ToDateTime(reader["proximo_vence"]);
                        DateTime fechaDesp = Convert.ToDateTime(reader["fecha_despacho"]);
                        int idCli = Convert.ToInt32(reader["id_cliente"]);

                        string idGenerado = "CTR-" + idCli.ToString("D4") + "-" + fechaDesp.ToString("yyMMdd");

                        lista.Add(new ContratoMaestroItem
                        {
                            IdCliente = idCli,
                            Empresa = reader["nombre_empresa"].ToString(),
                            Ruc = reader["ruc"].ToString(),
                            FechaDespacho = fechaDesp.ToString("dd/MM/yyyy"),
                            IdContratoGenerado = idGenerado,
                            EquiposAlquilados = Convert.ToInt32(reader["cantidad_equipos"]),
                            ValorTotalHardware = Convert.ToDecimal(reader["valor_total"]),
                            ProximoVencimiento = minVenc.ToString("dd/MM/yyyy"),
                            EquiposEnMora = Convert.ToInt32(reader["equipos_vencidos"]),
                            MoraTotalAcumulada = Convert.ToDecimal(reader["mora_total"])
                        });
                    }
                }
            }
            return View(lista);
        }



        // =======================================================
        // MÓDULO DE INFRAESTRUCTURA (SEDES Y UBICACIONES)
        // =======================================================
        public class ClienteSedesItem
        {
            public int IdCliente { get; set; }
            public string NombreEmpresa { get; set; }
            public string Ruc { get; set; }
            public int TotalSedes { get; set; }
        }

        public class SedeItem
        {
            public int IdSede { get; set; }
            public string NombreSede { get; set; }
            public string Departamento { get; set; }
            public string Observacion { get; set; }
            public int TotalAmbientes { get; set; }
        }

        public ActionResult Ubicaciones()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");

            var lista = new List<ClienteSedesItem>();
            var listaClientes = new List<SelectListItem>(); // Para el Modal de Nueva Sede

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();

                string sql = @"
                    SELECT 
                        c.id_cliente, 
                        c.nombre_empresa, 
                        c.ruc, 
                        (SELECT COUNT(*) FROM Sedes s WHERE s.id_cliente = c.id_cliente) AS total_sedes
                    FROM Clientes c 
                    WHERE c.activo = 1";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ClienteSedesItem
                        {
                            IdCliente = Convert.ToInt32(reader["id_cliente"]),
                            NombreEmpresa = reader["nombre_empresa"].ToString(),
                            Ruc = reader["ruc"].ToString(),
                            TotalSedes = Convert.ToInt32(reader["total_sedes"])
                        });

                        listaClientes.Add(new SelectListItem { Value = reader["id_cliente"].ToString(), Text = reader["nombre_empresa"].ToString() });
                    }
                }
            }

            ViewBag.ListaClientes = listaClientes;
            return View(lista);
        }

        public ActionResult SedesCliente(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            if (id == null) return RedirectToAction("Ubicaciones");

            var lista = new List<SedeItem>();
            string nombreEmpresa = "";

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();

                using (var cmdCli = new SqlCommand("SELECT nombre_empresa FROM Clientes WHERE id_cliente = @id", con))
                {
                    cmdCli.Parameters.AddWithValue("@id", id.Value);
                    var result = cmdCli.ExecuteScalar();
                    if (result == null) return RedirectToAction("Ubicaciones");
                    nombreEmpresa = result.ToString();
                }

                string sql = @"
                    SELECT 
                        s.id_sede, 
                        s.nombre_sede, 
                        ISNULL(s.departamento, 'No especificado') AS departamento, 
                        ISNULL(s.observacion, '') AS observacion,
                        (SELECT COUNT(*) FROM Ubicaciones_Internas u WHERE u.id_sede = s.id_sede) AS total_ambientes
                    FROM Sedes s 
                    WHERE s.id_cliente = @idCli
                    ORDER BY s.nombre_sede ASC";

                using (var cmd = new SqlCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@idCli", id.Value);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new SedeItem
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

            ViewBag.NombreEmpresa = nombreEmpresa;
            ViewBag.IdCliente = id.Value;
            return View(lista);
        }

        [HttpPost]
        public JsonResult GuardarSede(int idCliente, string nombreSede, string departamento, string observacion)
        {
            if (!EsAdmin()) return Json(new { success = false, message = "No autorizado." });

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

        [HttpPost]
        public JsonResult EditarSede(int idSede, string nombreSede, string departamento, string observacion)
        {
            if (!EsAdmin()) return Json(new { success = false, message = "No autorizado." });

            try
            {
                using (var con = new ConexionBD().ObtenerConexion())
                {
                    con.Open();
                    string sql = "UPDATE Sedes SET nombre_sede = @nom, departamento = @dep, observacion = @obs WHERE id_sede = @id";
                    using (var cmd = new SqlCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@id", idSede);
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

        [HttpPost]
        public JsonResult EliminarSede(int idSede)
        {
            if (!EsAdmin()) return Json(new { success = false, message = "No autorizado." });

            try
            {
                using (var con = new ConexionBD().ObtenerConexion())
                {
                    con.Open();
                    string checkSql = "SELECT COUNT(*) FROM Ubicaciones_Internas WHERE id_sede = @id";
                    using (var cmdCheck = new SqlCommand(checkSql, con))
                    {
                        cmdCheck.Parameters.AddWithValue("@id", idSede);
                        int count = (int)cmdCheck.ExecuteScalar();
                        if (count > 0)
                        {
                            return Json(new { success = false, message = "No se puede eliminar esta Sede porque tiene Ambientes y/o Equipos asignados a ella. Retire los equipos primero." });
                        }
                    }

                    string sql = "DELETE FROM Sedes WHERE id_sede = @id";
                    using (var cmd = new SqlCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@id", idSede);
                        cmd.ExecuteNonQuery();
                    }
                }
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // =======================================================
        // CRUD DE AMBIENTES (UBICACIONES INTERNAS POR SEDE)
        // =======================================================
        public class AmbienteItem
        {
            public int IdUbicacion { get; set; }
            public string NombreEspecifico { get; set; }
            public string TipoAmbiente { get; set; }
            public int TotalEquiposFisicos { get; set; }
        }

        public ActionResult AmbientesSede(int? id)
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            if (id == null) return RedirectToAction("Ubicaciones");

            var lista = new List<AmbienteItem>();
            string nombreSede = "";
            int idClientePadre = 0;

            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();

                using (var cmd = new SqlCommand("SELECT nombre_sede, id_cliente FROM Sedes WHERE id_sede = @id", con))
                {
                    cmd.Parameters.AddWithValue("@id", id.Value);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            nombreSede = reader["nombre_sede"].ToString();
                            idClientePadre = Convert.ToInt32(reader["id_cliente"]);
                        }
                        else return RedirectToAction("Ubicaciones");
                    }
                }

                string sql = @"
                    SELECT 
                        u.id_ubicacion, 
                        u.nombre_especifico, 
                        ISNULL(u.tipo_ambiente, 'General') AS tipo_ambiente,
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
                            lista.Add(new AmbienteItem
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
            ViewBag.IdClientePadre = idClientePadre;
            return View(lista);
        }

        [HttpPost]
        public JsonResult GuardarAmbiente(int idSede, string nombreEspecifico, string tipoAmbiente)
        {
            if (!EsAdmin()) return Json(new { success = false, message = "No autorizado." });
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

        [HttpPost]
        public JsonResult EditarAmbiente(int idUbicacion, string nombreEspecifico, string tipoAmbiente)
        {
            if (!EsAdmin()) return Json(new { success = false, message = "No autorizado." });
            try
            {
                using (var con = new ConexionBD().ObtenerConexion())
                {
                    con.Open();
                    string sql = "UPDATE Ubicaciones_Internas SET nombre_especifico = @nom, tipo_ambiente = @tipo WHERE id_ubicacion = @id";
                    using (var cmd = new SqlCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@id", idUbicacion);
                        cmd.Parameters.AddWithValue("@nom", nombreEspecifico.Trim());
                        cmd.Parameters.AddWithValue("@tipo", string.IsNullOrEmpty(tipoAmbiente) ? "Oficina/Área" : tipoAmbiente.Trim());
                        cmd.ExecuteNonQuery();
                    }
                }
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        public JsonResult EliminarAmbiente(int idUbicacion)
        {
            if (!EsAdmin()) return Json(new { success = false, message = "No autorizado." });
            try
            {
                using (var con = new ConexionBD().ObtenerConexion())
                {
                    con.Open();
                    using (var cmdCheck = new SqlCommand("SELECT COUNT(*) FROM Activos WHERE id_ubicacion = @id", con))
                    {
                        cmdCheck.Parameters.AddWithValue("@id", idUbicacion);
                        if ((int)cmdCheck.ExecuteScalar() > 0)
                            return Json(new { success = false, message = "No se puede eliminar este Ambiente porque hay activos físicos registrados aquí. Realice un traslado primero." });
                    }

                    using (var cmd = new SqlCommand("DELETE FROM Ubicaciones_Internas WHERE id_ubicacion = @id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", idUbicacion);
                        cmd.ExecuteNonQuery();
                    }
                }
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // =======================================================
        // MÓDULO DE COBRANZAS Y DEUDAS (NUEVO)
        // =======================================================
        public class ClienteDeudaItem
        {
            public int IdCliente { get; set; }
            public string Empresa { get; set; }
            public string Ruc { get; set; }
            public int EquiposEnMora { get; set; }
            public decimal TotalDeuda { get; set; }
        }

        public ActionResult Cobranzas()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            var lista = new List<ClienteDeudaItem>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                string sql = @"
                    SELECT 
                        c.id_cliente, c.nombre_empresa, c.ruc,
                        SUM(CASE WHEN r.fecha_vencimiento < GETDATE() AND a.id_estado = 2 THEN 1 ELSE 0 END) AS equipos_vencidos,
                        SUM(CASE WHEN r.fecha_vencimiento < GETDATE() AND a.id_estado = 2 THEN DATEDIFF(day, r.fecha_vencimiento, GETDATE()) * ISNULL(r.multa_diaria_usd, 1.00) ELSE 0 END) AS mora_total
                    FROM Clientes c
                    LEFT JOIN Registro_Alquiler r ON c.id_cliente = r.id_cliente
                    LEFT JOIN Activos a ON r.id_activo_actual = a.id_activo
                    WHERE c.activo = 1
                    GROUP BY c.id_cliente, c.nombre_empresa, c.ruc
                    ORDER BY mora_total DESC, c.nombre_empresa ASC";

                using (var cmd = new SqlCommand(sql, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ClienteDeudaItem
                        {
                            IdCliente = Convert.ToInt32(reader["id_cliente"]),
                            Empresa = reader["nombre_empresa"].ToString(),
                            Ruc = reader["ruc"].ToString(),
                            EquiposEnMora = Convert.ToInt32(reader["equipos_vencidos"]),
                            TotalDeuda = Convert.ToDecimal(reader["mora_total"])
                        });
                    }
                }
            }
            return View(lista);
        }

        [HttpGet]
        public JsonResult ObtenerDetalleDeudaCliente(int idCliente)
        {
            if (!EsAdmin()) return Json(new { error = "No autorizado" }, JsonRequestBehavior.AllowGet);
            var lista = new List<object>();
            using (var con = new ConexionBD().ObtenerConexion())
            {
                con.Open();
                // Agrupamos por contrato (fecha de despacho) para ver qué lotes están vencidos
                string sql = @"
                    SELECT 
                        CAST(r.fecha_ingreso AS DATE) AS fecha_despacho,
                        COUNT(r.id_registro) AS cantidad_equipos,
                        SUM(DATEDIFF(day, r.fecha_vencimiento, GETDATE()) * ISNULL(r.multa_diaria_usd, 1.00)) AS mora_contrato
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
                            DateTime fDespacho = Convert.ToDateTime(reader["fecha_despacho"]);
                            string contratoGenerado = "CTR-" + idCliente.ToString("D4") + "-" + fDespacho.ToString("yyMMdd");

                            // Aquí se soluciona el aviso IDE0037
                            lista.Add(new
                            {
                                contrato = contratoGenerado,
                                equipos = reader["cantidad_equipos"].ToString(),
                                mora = Convert.ToDecimal(reader["mora_contrato"])
                            });
                        }
                    }
                }
            }
            return Json(lista, JsonRequestBehavior.AllowGet);
        }

        // =======================================================
        // OTRAS VISTAS (Mantenimiento)
        // =======================================================
        public ActionResult Solicitudes() { if (!EsAdmin()) return RedirectToAction("Index", "Login"); return View(); }
        public ActionResult MiPerfil() { if (!EsAdmin()) return RedirectToAction("Index", "Login"); return View(); }
        public ActionResult Configuracion() { if (!EsAdmin()) return RedirectToAction("Index", "Login"); return View(); }
    }
}