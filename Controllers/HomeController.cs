using System;
using System.Diagnostics;
using GestionCabinetMedical.Models;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GestionCabinetMedical.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly BdCabinetMedicalContext _context;

        public HomeController(ILogger<HomeController> logger, BdCabinetMedicalContext context)
        {
            _logger = logger;
            _context = context;
        }

        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                // ========== UTILISATEUR CONNECTÉ ==========

                // 1. Récupérer l'email et l'utilisateur
                var emailConnecte = User.FindFirst(ClaimTypes.Email)?.Value
                                 ?? User.FindFirst(ClaimTypes.Name)?.Value
                                 ?? User.Identity.Name;

                var utilisateur = _context.Utilisateurs.FirstOrDefault(u => u.Email == emailConnecte);

                if (utilisateur != null)
                {
                    ViewBag.NomComplet = utilisateur.Prenom + " " + utilisateur.Nom;

                    // 2. Déterminer le rôle de l'utilisateur
                    var isAdmin = User.IsInRole("ADMIN");
                    var isMedecin = User.IsInRole("MEDECIN") && !User.IsInRole("ADMIN");
                    var isSecretaire = User.IsInRole("SECRETAIRE");
                    var isPatient = User.IsInRole("PATIENT");

                    // ========== STATISTIQUES SELON LE RÔLE ==========

                    // 3. Nombre de patients (Admin et Secrétaire uniquement)
                    if (isAdmin || isSecretaire)
                    {
                        ViewBag.NbPatients = _context.Patients.Count();
                    }

                    // 4. Rendez-vous aujourd'hui (FILTRÉ PAR RÔLE)
                    var today = DateTime.Today;

                    if (isPatient)
                    {
                        // Patient : uniquement SES rendez-vous
                        ViewBag.NbRdvAujourdhui = _context.RendezVous
                            .Where(r => r.PatientId == utilisateur.Id && r.DateHeure.Date == today)
                            .Count();
                    }
                    else if (isMedecin)
                    {
                        // Médecin : uniquement SES rendez-vous
                        ViewBag.NbRdvAujourdhui = _context.RendezVous
                            .Where(r => r.MedecinId == utilisateur.Id && r.DateHeure.Date == today)
                            .Count();
                    }
                    else if (isAdmin || isSecretaire)
                    {
                        // Admin et Secrétaire : TOUS les rendez-vous
                        ViewBag.NbRdvAujourdhui = _context.RendezVous
                            .Where(r => r.DateHeure.Date == today)
                            .Count();
                    }

                    // 5. Consultations (basées sur les rendez-vous terminés)
                    if (isAdmin)
                    {
                        // Admin : TOUTES les consultations
                        ViewBag.NbConsultations = _context.Consultations.Count();
                    }
                    else if (isMedecin)
                    {
                        // Médecin : SES consultations (= SES rendez-vous terminés)
                        ViewBag.NbConsultations = _context.RendezVous
                            .Where(r => r.MedecinId == utilisateur.Id && r.Statut == "Terminé")
                            .Count();
                    }

                    // 6. Nombre de médecins (Admin uniquement)
                    if (isAdmin)
                    {
                        ViewBag.NbMedecins = _context.Medecins.Count();
                    }
                }
                else
                {
                    ViewBag.NomComplet = emailConnecte;
                }
            }
            else
            {
                // ========== PAGE PUBLIQUE (NON CONNECTÉ) ==========

                // Statistiques publiques du cabinet
                ViewBag.NombreMedecins = _context.Medecins.Count();
                ViewBag.NombrePatients = _context.Patients.Count();

                // Consultations ce mois (basé sur les RDV terminés)
                var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                ViewBag.ConsultationsCeMois = _context.RendezVous
                    .Where(r => r.DateHeure >= startOfMonth && r.Statut == "Terminé")
                    .Count();

                // Liste des spécialités (pour la page publique)
                ViewBag.Specialites = _context.Medecins
                    .Where(m => !string.IsNullOrEmpty(m.Specialite))
                    .Select(m => m.Specialite)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();
            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}