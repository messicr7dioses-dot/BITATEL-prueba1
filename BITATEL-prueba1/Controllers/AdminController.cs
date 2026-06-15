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
            var user = Session["UsuarioActivo"] as Usuario;
            // Verificamos que la sesión no sea nula y que el usuario sea Admin (Rol 1)
            return user != null && user.IdRol == 1;
        }

        // =======================================================
        // ADMINISTRACIÓN PRINCIPAL
        // =======================================================
        public ActionResult Index()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }

        public ActionResult RegistrarAlquiler()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }

        public ActionResult GestionarStock()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }

        public ActionResult Usuarios()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }

        // =======================================================
        // CONSULTAS Y LOGÍSTICA
        // =======================================================
        public ActionResult Ubicaciones()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }

        public ActionResult Contratos()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }

        public ActionResult Solicitudes()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }

        // =======================================================
        // OPCIONES DE USUARIO (Dropdown)
        // =======================================================
        public ActionResult MiPerfil()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }

        public ActionResult Configuracion()
        {
            if (!EsAdmin()) return RedirectToAction("Index", "Login");
            return View();
        }
    }
}