using GestionCabinetMedical.Models;

namespace GestionCabinetMedical.ViewModels
{
    public class RendezVousIndexViewModel
    {
        // Liste paginée des rendez-vous
        public PaginatedList<RendezVou>? RendezVous { get; set; }

        // Filtres
        public string? SearchTerm { get; set; }
        public string? Statut { get; set; }
        public string? Periode { get; set; }
        public DateTime? DateDebut { get; set; }
        public DateTime? DateFin { get; set; }
        public int? MedecinId { get; set; }

        // Tri et pagination
        public string SortBy { get; set; } = "date";
        public string SortOrder { get; set; } = "asc";
        public int PageSize { get; set; } = 10;

        // Rôles utilisateur
        public bool IsPatient { get; set; }
        public bool IsMedecin { get; set; }
        public bool IsAdmin { get; set; }

        // Liste des médecins pour le filtre
        public List<MedecinSelectItem> Medecins { get; set; } = new List<MedecinSelectItem>();

        // Statistiques
        public int TotalRendezVous { get; set; }
        public int RendezVousAujourdHui { get; set; }
        public int RendezVousEnAttente { get; set; }
        public int RendezVousConfirmes { get; set; }
        public int RendezVousAnnules { get; set; }
        public int RendezVousTermines { get; set; }
        public int RendezVousCetteSemaine { get; set; }

        // Helper pour le tri
        public string GetSortIcon(string columnName)
        {
            if (SortBy != columnName)
                return "bi-arrow-down-up";

            return SortOrder == "asc" ? "bi-arrow-up" : "bi-arrow-down";
        }

        // Helper pour le nouvel ordre de tri
        public string GetNewSortOrder(string columnName)
        {
            if (SortBy != columnName)
                return "asc";

            return SortOrder == "asc" ? "desc" : "asc";
        }
    }

    public class MedecinSelectItem
    {
        public int Id { get; set; }
        public string Nom { get; set; } = string.Empty;
        public string Specialite { get; set; } = string.Empty;

        public string NomComplet => $"Dr. {Nom} ({Specialite})";
    }
}