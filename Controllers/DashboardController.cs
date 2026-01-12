using GestionCabinetMedical.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GestionCabinetMedical.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly BdCabinetMedicalContext _context;

        public DashboardController(BdCabinetMedicalContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Helper : Récupérer l'ID utilisateur connecté
        /// </summary>
        private async Task<int> GetCurrentUserIdAsync()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value
                     ?? User.FindFirst(ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(email))
                return 0;

            var utilisateur = await _context.Utilisateurs
                .FirstOrDefaultAsync(u => u.Email == email);

            return utilisateur?.Id ?? 0;
        }

        /// <summary>
        /// Point d'entrée principal - Route vers le dashboard approprié selon le rôle
        /// Pattern: Strategy Pattern pour la sélection du dashboard
        /// </summary>
        public async Task<IActionResult> Index()
        {
            if (User.IsInRole("ADMIN"))
                return await AdminDashboard();

            if (User.IsInRole("MEDECIN"))
                return await MedecinDashboard();

            if (User.IsInRole("SECRETAIRE"))
                return await SecretaireDashboard();

            if (User.IsInRole("PATIENT"))
                return await PatientDashboard();

            // Utilisateur sans rôle spécifique
            return await PublicDashboard();
        }

        // ============================================================
        // DASHBOARD ADMIN - Vue complète du cabinet (DASHBOARD ACTUEL)
        // ============================================================
        [Authorize(Roles = "ADMIN")]
        private async Task<IActionResult> AdminDashboard()
        {
            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);

            // Statistiques générales
            ViewBag.TotalPatients = await _context.Patients.CountAsync();
            ViewBag.TotalMedecins = await _context.Medecins.CountAsync();
            ViewBag.TotalRdvAujourdHui = await _context.RendezVous
                .CountAsync(r => r.DateHeure.Date == today);
            ViewBag.TotalConsultationsMois = await _context.Consultations
                .CountAsync(c => c.DateConsultation >= startOfMonth);

            // Rendez-vous par statut
            ViewBag.RdvEnAttente = await _context.RendezVous
                .CountAsync(r => r.Statut == "En attente" && r.DateHeure >= today);
            ViewBag.RdvConfirmes = await _context.RendezVous
                .CountAsync(r => r.Statut == "Confirmé" && r.DateHeure >= today);
            ViewBag.RdvAnnules = await _context.RendezVous
                .CountAsync(r => r.Statut == "Annulé" && r.DateHeure >= startOfMonth);
            ViewBag.RdvTermines = await _context.RendezVous
                .CountAsync(r => r.Statut == "Terminé" && r.DateHeure >= startOfMonth);

            // Prochains rendez-vous (7 prochains)
            var prochainRdv = await _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .Where(r => r.DateHeure >= DateTime.Now && r.Statut != "Annulé")
                .OrderBy(r => r.DateHeure)
                .Take(7)
                .ToListAsync();
            ViewBag.ProchainsRdv = prochainRdv;

            // Derniers patients inscrits
            var derniersPatients = await _context.Patients
                .Include(p => p.IdNavigation)
                .OrderByDescending(p => p.Id)
                .Take(5)
                .ToListAsync();
            ViewBag.DerniersPatients = derniersPatients;

            // Statistiques par mois (6 derniers mois)
            var stats = new List<object>();
            for (int i = 5; i >= 0; i--)
            {
                var month = today.AddMonths(-i);
                var startMonth = new DateTime(month.Year, month.Month, 1);
                var endMonth = startMonth.AddMonths(1);

                var rdvCount = await _context.RendezVous
                    .CountAsync(r => r.DateHeure >= startMonth && r.DateHeure < endMonth);
                var consultCount = await _context.Consultations
                    .CountAsync(c => c.DateConsultation >= startMonth && c.DateConsultation < endMonth);

                stats.Add(new
                {
                    Mois = startMonth.ToString("MMM yyyy"),
                    RendezVous = rdvCount,
                    Consultations = consultCount
                });
            }
            ViewBag.StatsMensuelles = stats;

            // Top 5 médecins par nombre de consultations ce mois
            var topMedecins = await _context.DossierMedicals
                .Include(d => d.Medecin).ThenInclude(m => m.IdNavigation)
                .Include(d => d.Consultations)
                .Where(d => d.Consultations.Any(c => c.DateConsultation >= startOfMonth))
                .GroupBy(d => d.Medecin)
                .Select(g => new
                {
                    Medecin = g.Key,
                    NbConsultations = g.SelectMany(d => d.Consultations)
                        .Count(c => c.DateConsultation >= startOfMonth)
                })
                .OrderByDescending(x => x.NbConsultations)
                .Take(5)
                .ToListAsync();
            ViewBag.TopMedecins = topMedecins;

            ViewBag.DashboardType = "Admin";
            return View("AdminDashboard");
        }

        // ============================================================
        // DASHBOARD PATIENT - Informations personnelles uniquement
        // ============================================================
        [Authorize(Roles = "PATIENT")]
        private async Task<IActionResult> PatientDashboard()
        {
            var patientId = await GetCurrentUserIdAsync();
            if (patientId == 0)
                return RedirectToAction("Login", "Account");

            var patient = await _context.Patients
                .Include(p => p.IdNavigation)
                .Include(p => p.RendezVous)
                    .ThenInclude(r => r.Medecin)
                        .ThenInclude(m => m.IdNavigation)
                .Include(p => p.DossierMedicals)
                    .ThenInclude(d => d.Consultations)
                .FirstOrDefaultAsync(p => p.Id == patientId);

            if (patient == null)
                return RedirectToAction("Login", "Account");

            // Statistiques personnelles
            ViewBag.TotalRdv = patient.RendezVous?.Count ?? 0;
            ViewBag.ProchainRdv = patient.RendezVous?
                .Where(r => r.DateHeure >= DateTime.Now && r.Statut != "Annulé")
                .OrderBy(r => r.DateHeure)
                .FirstOrDefault();

            ViewBag.RdvAVenir = patient.RendezVous?
                .Count(r => r.DateHeure >= DateTime.Now && r.Statut != "Annulé") ?? 0;

            ViewBag.RdvEnAttente = patient.RendezVous?
                .Count(r => r.Statut == "En attente" && r.DateHeure >= DateTime.Now) ?? 0;

            // Derniers rendez-vous
            ViewBag.DerniersRdv = patient.RendezVous?
                .OrderByDescending(r => r.DateHeure)
                .Take(5)
                .ToList();

            // Dernières consultations
            ViewBag.DernieresConsultations = patient.DossierMedicals?
                .SelectMany(d => d.Consultations ?? Enumerable.Empty<Consultation>())
                .OrderByDescending(c => c.DateConsultation)
                .Take(3)
                .ToList();

            // Informations du patient
            ViewBag.Patient = patient;
            ViewBag.DashboardType = "Patient";

            return View("PatientDashboard");
        }

        // ============================================================
        // DASHBOARD MEDECIN - Statistiques et patients du médecin
        // ============================================================
        [Authorize(Roles = "MEDECIN")]
        private async Task<IActionResult> MedecinDashboard()
        {
            var medecinId = await GetCurrentUserIdAsync();
            if (medecinId == 0)
                return RedirectToAction("Login", "Account");

            var medecin = await _context.Medecins
                .Include(m => m.IdNavigation)
                .Include(m => m.RendezVous)
                    .ThenInclude(r => r.Patient)
                        .ThenInclude(p => p.IdNavigation)
                .Include(m => m.DossierMedicals)
                    .ThenInclude(d => d.Patient)
                        .ThenInclude(p => p.IdNavigation)
                .Include(m => m.DossierMedicals)
                    .ThenInclude(d => d.Consultations)
                .FirstOrDefaultAsync(m => m.Id == medecinId);

            if (medecin == null)
                return RedirectToAction("Login", "Account");

            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            // Statistiques du médecin
            ViewBag.TotalRdv = medecin.RendezVous?.Count ?? 0;
            ViewBag.RdvAujourdhui = medecin.RendezVous?
                .Count(r => r.DateHeure.Date == today && r.Statut != "Annulé") ?? 0;
            ViewBag.RdvAVenir = medecin.RendezVous?
                .Count(r => r.DateHeure > DateTime.Now && r.Statut != "Annulé") ?? 0;
            ViewBag.RdvCeMois = medecin.RendezVous?
                .Count(r => r.DateHeure >= startOfMonth && r.DateHeure.Month == DateTime.Now.Month) ?? 0;

            ViewBag.TotalPatients = medecin.DossierMedicals?
                .Select(d => d.PatientId)
                .Distinct()
                .Count() ?? 0;

            ViewBag.TotalConsultations = medecin.DossierMedicals?
                .Sum(d => d.Consultations?.Count ?? 0) ?? 0;

            ViewBag.ConsultationsCeMois = medecin.DossierMedicals?
                .SelectMany(d => d.Consultations ?? Enumerable.Empty<Consultation>())
                .Count(c => c.DateConsultation >= startOfMonth) ?? 0;

            // RDV par statut
            ViewBag.RdvEnAttente = medecin.RendezVous?
                .Count(r => r.Statut == "En attente" && r.DateHeure >= today) ?? 0;
            ViewBag.RdvConfirmes = medecin.RendezVous?
                .Count(r => r.Statut == "Confirmé" && r.DateHeure >= today) ?? 0;

            // RDV du jour
            ViewBag.RdvDuJour = medecin.RendezVous?
                .Where(r => r.DateHeure.Date == today && r.Statut != "Annulé")
                .OrderBy(r => r.DateHeure)
                .ToList();

            // Prochains RDV
            ViewBag.ProchainsRdv = medecin.RendezVous?
                .Where(r => r.DateHeure > DateTime.Now && r.Statut != "Annulé")
                .OrderBy(r => r.DateHeure)
                .Take(5)
                .ToList();

            // Patients récents
            ViewBag.PatientsRecents = medecin.DossierMedicals?
                .OrderByDescending(d => d.NumDossier)
                .Select(d => d.Patient)
                .Where(p => p != null)
                .DistinctBy(p => p.Id)
                .Take(5)
                .ToList();

            ViewBag.Medecin = medecin;
            ViewBag.DashboardType = "Medecin";

            return View("MedecinDashboard");
        }

        // ============================================================
        // DASHBOARD SECRETAIRE - Gestion des RDV et patients
        // ============================================================
        [Authorize(Roles = "SECRETAIRE")]
        private async Task<IActionResult> SecretaireDashboard()
        {
            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            // Statistiques RDV
            ViewBag.RdvAujourdhui = await _context.RendezVous
                .CountAsync(r => r.DateHeure.Date == today);
            ViewBag.RdvEnAttente = await _context.RendezVous
                .CountAsync(r => r.Statut == "En attente" && r.DateHeure >= today);
            ViewBag.RdvConfirmes = await _context.RendezVous
                .CountAsync(r => r.Statut == "Confirmé" && r.DateHeure >= today);
            ViewBag.RdvCeMois = await _context.RendezVous
                .CountAsync(r => r.DateHeure >= startOfMonth);

            // RDV du jour
            ViewBag.RdvDuJour = await _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .Where(r => r.DateHeure.Date == today)
                .OrderBy(r => r.DateHeure)
                .ToListAsync();

            // RDV en attente de confirmation
            ViewBag.RdvEnAttenteList = await _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .Where(r => r.Statut == "En attente" && r.DateHeure >= DateTime.Now)
                .OrderBy(r => r.DateHeure)
                .Take(10)
                .ToListAsync();

            // Prochains RDV
            ViewBag.ProchainsRdv = await _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .Where(r => r.DateHeure > DateTime.Now && r.Statut != "Annulé")
                .OrderBy(r => r.DateHeure)
                .Take(7)
                .ToListAsync();

            // Statistiques patients
            ViewBag.TotalPatients = await _context.Patients.CountAsync();
            ViewBag.NouveauxPatientsMois = await _context.Patients
                .CountAsync(p => p.Id > 0); // Ajustez selon votre logique de date de création

            ViewBag.DashboardType = "Secretaire";

            return View("SecretaireDashboard");
        }

        // ============================================================
        // DASHBOARD PUBLIC - Informations générales sur le cabinet
        // ============================================================
        [AllowAnonymous]
        private async Task<IActionResult> PublicDashboard()
        {
            // Informations générales du cabinet
            ViewBag.NombreMedecins = await _context.Medecins.CountAsync();
            ViewBag.NombrePatients = await _context.Patients.CountAsync();

            // Spécialités disponibles
            ViewBag.Specialites = await _context.Medecins
                .Where(m => !string.IsNullOrEmpty(m.Specialite))
                .Select(m => m.Specialite)
                .Distinct()
                .ToListAsync();

            // Top médecins (basé sur le nombre de consultations)
            var startOfYear = new DateTime(DateTime.Now.Year, 1, 1);
            ViewBag.TopMedecins = await _context.DossierMedicals
                .Include(d => d.Medecin).ThenInclude(m => m.IdNavigation)
                .Include(d => d.Consultations)
                .Where(d => d.Consultations.Any())
                .GroupBy(d => d.Medecin)
                .Select(g => new
                {
                    Medecin = g.Key,
                    NbConsultations = g.SelectMany(d => d.Consultations).Count(),
                    Specialite = g.Key.Specialite
                })
                .OrderByDescending(x => x.NbConsultations)
                .Take(5)
                .ToListAsync();

            // Statistiques générales (anonymisées)
            var thisMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            ViewBag.ConsultationsCeMois = await _context.Consultations
                .CountAsync(c => c.DateConsultation >= thisMonth);

            ViewBag.DashboardType = "Public";

            return View("PublicDashboard");
        }

        // ============================================================
        // API pour graphiques (utilisé par tous les dashboards)
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> GetStatsRdvParJour()
        {
            var today = DateTime.Today;
            var data = new List<object>();

            // Filtrer selon le rôle
            IQueryable<RendezVou> query = _context.RendezVous;

            if (User.IsInRole("MEDECIN"))
            {
                var medecinId = await GetCurrentUserIdAsync();
                query = query.Where(r => r.MedecinId == medecinId);
            }
            else if (User.IsInRole("PATIENT"))
            {
                var patientId = await GetCurrentUserIdAsync();
                query = query.Where(r => r.PatientId == patientId);
            }

            for (int i = 6; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                var count = await query.CountAsync(r => r.DateHeure.Date == date);

                data.Add(new
                {
                    Date = date.ToString("dd/MM"),
                    Count = count
                });
            }

            return Json(data);
        }

        [HttpGet]
        public async Task<IActionResult> GetStatsRdvParStatut()
        {
            IQueryable<RendezVou> query = _context.RendezVous;

            // Filtrer selon le rôle
            if (User.IsInRole("MEDECIN"))
            {
                var medecinId = await GetCurrentUserIdAsync();
                query = query.Where(r => r.MedecinId == medecinId);
            }
            else if (User.IsInRole("PATIENT"))
            {
                var patientId = await GetCurrentUserIdAsync();
                query = query.Where(r => r.PatientId == patientId);
            }

            var stats = await query
                .GroupBy(r => r.Statut)
                .Select(g => new
                {
                    Statut = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            return Json(stats);
        }
    }
}