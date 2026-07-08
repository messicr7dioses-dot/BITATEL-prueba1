using System;
using System.Data.SqlClient;
using System.Web.Mvc;
using BITATEL_prueba1.Models;

namespace BITATEL_prueba1.Controllers
{
    public class LoginController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Entrar(string username, string password)
        {
            Usuario userLogueado = null;
            ConexionBD objConexion = new ConexionBD();

            using (SqlConnection con = objConexion.ObtenerConexion())
            {
                string query = "SELECT id_usuario, nombre_completo, id_rol FROM Usuarios WHERE username = @user AND password_hash = @pass";
                SqlCommand cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@user", username);
                cmd.Parameters.AddWithValue("@pass", password);

                con.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
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

            if (userLogueado != null)
            {
                Session["UsuarioActivo"] = userLogueado;

             
                if (userLogueado.IdRol == 1)
                    return RedirectToAction("Index", "Admin");
                else if (userLogueado.IdRol == 2)
                    return RedirectToAction("Index", "Cliente");
                else
                    return RedirectToAction("Index", "Home");
            }
            else
            {
                ViewBag.Error = "Usuario o contraseña incorrectos.";
                return View("Index");
            }
        }


        public ActionResult Salir()
        {
            Session.Clear();
            return RedirectToAction("Index", "Login");
        }
    }
}