namespace BITATEL_prueba1.Models
{
    public class DashboardAdminViewModel
    {
        public int TotalActivos { get; set; }
        public int ActivosEnStock { get; set; }
        public int ActivosAlquilados { get; set; }
        public int ContratosPorVencer { get; set; }
        public decimal TotalDeudaPendiente { get; set; }
        public int ClientesActivos { get; set; }
    }
}