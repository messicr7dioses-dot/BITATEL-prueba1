using System;
using System.Data.SqlClient;
using System.Web.Mvc;
using BITATEL_prueba1.Models;

namespace BITATEL_prueba1.Controllers
{
    public class LoginController : Controller
    {
        // 1. Dibuja la pantalla de Login (GET)
        public ActionResult Index()
        {
            return View();
        }

        // 2. Procesa los datos cuando el usuario presiona "Ingresar" (POST)
        [HttpPost]
        public ActionResult Entrar(string username, string password)
        {
            Usuario userLogueado = null;
            ConexionBD objConexion = new ConexionBD();

            // Usamos bloque 'using' para asegurar que la conexión se cierre y libere memoria automáticamente
            using (SqlConnection con = objConexion.ObtenerConexion())
            {
                // Consulta SQL parametrizada para evitar inyecciones de código malicioso
                string query = "SELECT id_usuario, nombre_completo, id_rol FROM Usuarios WHERE username = @user AND password_hash = @pass";
                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@user", username);
                cmd.Parameters.AddWithValue("@pass", password);

                con.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    // Si el reader encuentra una fila, las credenciales son correctas
                    if (reader.Read())
                    {
                        // Inicialización simplificada de objetos
                        userLogueado = new Usuario
                        {
                            IdUsuario = Convert.ToInt64(reader["id_usuario"]),
                            NombreCompleto = reader["nombre_completo"].ToString(),
                            Username = username,
                            IdRol = Convert.ToInt64(reader["id_rol"])
                        };
                    }
                }
            }

            // 3. Evaluamos el resultado
            if (userLogueado != null)
            {
                // Guardamos el objeto entero en la Sesión del navegador
                Session["UsuarioActivo"] = userLogueado;

                // Ruteo inteligente según el Rol (1 = Admin, 2 = Cliente)
                if (userLogueado.IdRol == 1)
                    return RedirectToAction("Index", "Admin");
                else if (userLogueado.IdRol == 2)
                    return RedirectToAction("Index", "Cliente");
                else
                    return RedirectToAction("Index", "Home");
            }
            else
            {
                // Si userLogueado sigue en null, las credenciales fallaron
                ViewBag.Error = "Usuario o contraseña incorrectos.";
                return View("Index");
            }
        }

        // 3. Método para destruir la sesión y salir
        public ActionResult Salir()
        {
            Session.Clear();
            return RedirectToAction("Index", "Login");
        }
    }
}