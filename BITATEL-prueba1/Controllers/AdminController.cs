using System.Web.Mvc;
using BITATEL_prueba1.Models;

namespace BITATEL_prueba1.Controllers
{
    public class AdminController : Controller
    {
        // Solo dejamos esto para verificar que el acceso funciona
        public ActionResult Index()
        {
            if (Session["UsuarioActivo"] == null) return RedirectToAction("Index", "Login");
            return View();
        }

        public ActionResult RegistrarAlquiler()
        {
            if (Session["UsuarioActivo"] == null) return RedirectToAction("Index", "Login");
            return View();
        }
    }
}