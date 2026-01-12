using GestionCabinetMedical.Models;

namespace GestionCabinetMedical.ViewModels
{
    public class DashboardViewModel
    {
        // État de la page
        public bool IsPublicPage { get; set; }
        public bool IsAuthenticated { get; set; }

        // Rôles utilisateur
        public bool IsAdmin { get; set; }
        public bool IsMedecin { get; set; }
        public bool IsPatient { get; set; }
        public bool IsSecretaire { get; set; }

        // Statistiques générales
        public int TotalRendezVous { get; set; }
        public int RendezVousAujourdhui { get; set; }
        public int RendezVousEnAttente { get; set; }
        public int RendezVousConfirmes { get; set; }
        public int RendezVousAnnules { get; set; }
        public int RendezVousCetteSemaine { get; set; }

        // Statistiques supplémentaires (pour Admin/Secrétaire)
        public int TotalPatients { get; set; }
        public int TotalMedecins { get; set; }

        // Rendez-vous
        public RendezVou? ProchainRendezVous { get; set; }
        public List<RendezVou> RendezVousRecents { get; set; } = new List<RendezVou>();

        // Helpers
        public string GetRoleLabel()
        {
            if (IsAdmin) return "Administrateur";
            if (IsMedecin) return "Médecin";
            if (IsSecretaire) return "Secrétaire";
            if (IsPatient) return "Patient";
            return "Utilisateur";
        }

        public string GetWelcomeMessage()
        {
            var timeOfDay = DateTime.Now.Hour switch
            {
                < 12 => "Bonjour",
                < 18 => "Bon après-midi",
                _ => "Bonsoir"
            };

            return $"{timeOfDay}, {GetRoleLabel()}";
        }
    }
}