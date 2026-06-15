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
            // Uso de Pattern Matching: Comprueba si es nulo, lo convierte a la clase Usuario 
            // y lo guarda en la variable 'user' en una sola línea.
            return Session["UsuarioActivo"] is Usuario user && user.IdRol == 1;
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