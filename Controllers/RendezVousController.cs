using GestionCabinetMedical.Models;
using GestionCabinetMedical.ViewModels;
using GestionCabinetMedical.Helpers;
using GestionCabinetMedical.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.Security.Claims;

namespace GestionCabinetMedical.Controllers
{
    [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE,PATIENT")]
    public class RendezVousController : Controller
    {
        private readonly BdCabinetMedicalContext _context;
        private readonly ICrudNotificationHelper _notificationHelper;
        private readonly INotificationService _notificationService;

        public RendezVousController(
            BdCabinetMedicalContext context,
            ICrudNotificationHelper notificationHelper,
            INotificationService notificationService)
        {
            _context = context;
            _notificationHelper = notificationHelper;
            _notificationService = notificationService;
        }

        // ============================================================
        // Helper : Récupérer l'ID utilisateur connecté
        // ============================================================
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

        // ============================================================
        // Helper : Vérifier si l'utilisateur est un patient
        // ============================================================
        private bool IsPatient()
        {
            return User.IsInRole("PATIENT") && !User.IsInRole("ADMIN") &&
                   !User.IsInRole("MEDECIN") && !User.IsInRole("SECRETAIRE");
        }

        // ============================================================
        // Helper : Vérifier si l'utilisateur est un médecin (non-admin)
        // ============================================================
        private bool IsMedecinOnly()
        {
            return User.IsInRole("MEDECIN") &&
                   !User.IsInRole("ADMIN") &&
                   !User.IsInRole("SECRETAIRE");
        }

        // ============================================================
        // Helper : Vérifier conflit de RDV
        // ============================================================
        private async Task<(bool hasConflict, string message)> CheckRdvConflictAsync(
            int medecinId, int patientId, DateTime dateHeure, int? excludeRdvId = null)
        {

            var startTime = dateHeure.AddMinutes(-29);
            var endTime = dateHeure.AddMinutes(29);

            // Vérifier conflit pour le médecin
            var medecinConflict = await _context.RendezVous
                .Where(r => r.MedecinId == medecinId
                         && r.DateHeure >= startTime
                         && r.DateHeure <= endTime
                         && r.Statut != "Annulé"
                         && (excludeRdvId == null || r.NumCom != excludeRdvId))
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .FirstOrDefaultAsync();

            if (medecinConflict != null)
            {
                var patientNom = $"{medecinConflict.Patient?.IdNavigation?.Nom} {medecinConflict.Patient?.IdNavigation?.Prenom}";
                return (true, $"Le médecin a déjà un rendez-vous à {medecinConflict.DateHeure:HH:mm} avec {patientNom}.");
            }

            // Vérifier conflit pour le patient
            var patientConflict = await _context.RendezVous
                .Where(r => r.PatientId == patientId
                         && r.DateHeure >= startTime
                         && r.DateHeure <= endTime
                         && r.Statut != "Annulé"
                         && (excludeRdvId == null || r.NumCom != excludeRdvId))
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .FirstOrDefaultAsync();

            if (patientConflict != null)
            {
                var medecinNom = $"Dr. {patientConflict.Medecin?.IdNavigation?.Nom}";
                return (true, $"Le patient a déjà un rendez-vous à {patientConflict.DateHeure:HH:mm} avec {medecinNom}.");
            }

            return (false, string.Empty);
        }

        // ============================================================
        // GET: RendezVous - Index avec pagination
        // ============================================================
        public async Task<IActionResult> Index(
            string? searchTerm,
            string? statut,
            string? periode,
            DateTime? dateDebut,
            DateTime? dateFin,
            int? medecinId,
            string sortBy = "date",
            string sortOrder = "asc",
            int page = 1,
            int pageSize = 10)
        {
            // ========== RESTRICTION PAR RÔLE ==========
            var isPatient = IsPatient();
            var isMedecin = IsMedecinOnly();
            var currentUserId = 0;

            if (isPatient || isMedecin)
            {
                currentUserId = await GetCurrentUserIdAsync();
                if (currentUserId == 0)
                {
                    return RedirectToAction("Login", "Account");
                }
            }

            // ========== QUERY DE BASE ==========
            var query = _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .AsQueryable();

            // ========== RESTRICTION D'ACCÈS PAR RÔLE ==========
            if (isPatient)
            {
                // PATIENT voit uniquement SES rendez-vous
                query = query.Where(r => r.PatientId == currentUserId);
            }
            else if (isMedecin)
            {
                // MÉDECIN voit uniquement SES rendez-vous
                query = query.Where(r => r.MedecinId == currentUserId);
            }
            // ADMIN et SECRETAIRE voient TOUS les rendez-vous (pas de filtre)

            // ============================================================
            // Calcul des statistiques (avec restriction par rôle)
            // ============================================================
            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(7);

            // Appliquer les mêmes restrictions pour les statistiques
            IQueryable<RendezVou> statsQuery = _context.RendezVous;

            if (isPatient)
            {
                statsQuery = statsQuery.Where(r => r.PatientId == currentUserId);
            }
            else if (isMedecin)
            {
                statsQuery = statsQuery.Where(r => r.MedecinId == currentUserId);
            }

            var allRdvForStats = await statsQuery.ToListAsync();

            var stats = new
            {
                Total = allRdvForStats.Count,
                Aujourdhui = allRdvForStats.Count(r => r.DateHeure.Date == today),
                EnAttente = allRdvForStats.Count(r => r.Statut == "En attente"),
                Confirmes = allRdvForStats.Count(r => r.Statut == "Confirmé"),
                Annules = allRdvForStats.Count(r => r.Statut == "Annulé"),
                Termines = allRdvForStats.Count(r => r.Statut == "Terminé"),
                CetteSemaine = allRdvForStats.Count(r => r.DateHeure >= startOfWeek && r.DateHeure < endOfWeek)
            };

            // ============================================================
            // Filtre par période rapide
            // ============================================================
            if (!string.IsNullOrEmpty(periode))
            {
                switch (periode)
                {
                    case "aujourd'hui":
                        query = query.Where(r => r.DateHeure.Date == today);
                        break;
                    case "semaine":
                        query = query.Where(r => r.DateHeure >= startOfWeek && r.DateHeure < endOfWeek);
                        break;
                    case "mois":
                        var startOfMonth = new DateTime(today.Year, today.Month, 1);
                        var endOfMonth = startOfMonth.AddMonths(1);
                        query = query.Where(r => r.DateHeure >= startOfMonth && r.DateHeure < endOfMonth);
                        break;
                }
            }

            // ============================================================
            // Filtres
            // ============================================================

            // Recherche textuelle
            if (!string.IsNullOrEmpty(searchTerm))
            {
                var search = searchTerm.ToLower();
                query = query.Where(r =>
                    (r.Patient != null && r.Patient.IdNavigation != null &&
                        (r.Patient.IdNavigation.Nom!.ToLower().Contains(search) ||
                         r.Patient.IdNavigation.Prenom!.ToLower().Contains(search))) ||
                    (r.Medecin != null && r.Medecin.IdNavigation != null &&
                        (r.Medecin.IdNavigation.Nom!.ToLower().Contains(search) ||
                         r.Medecin.IdNavigation.Prenom!.ToLower().Contains(search))) ||
                    (r.Motif != null && r.Motif.ToLower().Contains(search))
                );
            }

            // Filtre par statut
            if (!string.IsNullOrEmpty(statut))
            {
                query = query.Where(r => r.Statut == statut);
            }

            // Filtre par date de début
            if (dateDebut.HasValue)
            {
                query = query.Where(r => r.DateHeure.Date >= dateDebut.Value.Date);
            }

            // Filtre par date de fin
            if (dateFin.HasValue)
            {
                query = query.Where(r => r.DateHeure.Date <= dateFin.Value.Date);
            }

            // Filtre par médecin (uniquement pour ADMIN et SECRETAIRE)
            if (medecinId.HasValue && !isMedecin)
            {
                query = query.Where(r => r.MedecinId == medecinId.Value);
            }

            // ============================================================
            // Tri
            // ============================================================
            query = (sortBy, sortOrder) switch
            {
                ("date", "asc") => query.OrderBy(r => r.DateHeure),
                ("date", "desc") => query.OrderByDescending(r => r.DateHeure),
                ("patient", "asc") => query.OrderBy(r => r.Patient!.IdNavigation!.Nom),
                ("patient", "desc") => query.OrderByDescending(r => r.Patient!.IdNavigation!.Nom),
                ("medecin", "asc") => query.OrderBy(r => r.Medecin!.IdNavigation!.Nom),
                ("medecin", "desc") => query.OrderByDescending(r => r.Medecin!.IdNavigation!.Nom),
                ("statut", "asc") => query.OrderBy(r => r.Statut),
                ("statut", "desc") => query.OrderByDescending(r => r.Statut),
                _ => query.OrderBy(r => r.DateHeure)
            };

            // ============================================================
            // Pagination
            // ============================================================
            var paginatedList = await PaginatedList<RendezVou>.CreateAsync(query, page, pageSize);

            // ============================================================
            // Liste des médecins pour le filtre (masquée pour les médecins)
            // ============================================================
            var medecins = new List<MedecinSelectItem>();

            if (!isMedecin) // Les médecins n'ont pas besoin de ce filtre
            {
                medecins = await _context.Medecins
                    .Include(m => m.IdNavigation)
                    .Where(m => m.IdNavigation != null)
                    .Select(m => new MedecinSelectItem
                    {
                        Id = m.Id,
                        Nom = m.IdNavigation!.Nom ?? "",
                        Specialite = m.Specialite ?? ""
                    })
                    .OrderBy(m => m.Nom)
                    .ToListAsync();
            }

            // ============================================================
            // ViewModel
            // ============================================================
            var viewModel = new RendezVousIndexViewModel
            {
                RendezVous = paginatedList,
                SearchTerm = searchTerm,
                Statut = statut,
                Periode = periode,
                DateDebut = dateDebut,
                DateFin = dateFin,
                MedecinId = medecinId,
                SortBy = sortBy,
                SortOrder = sortOrder,
                PageSize = pageSize,
                IsPatient = isPatient,
                IsMedecin = isMedecin,
                Medecins = medecins,

                // Statistiques
                TotalRendezVous = stats.Total,
                RendezVousAujourdHui = stats.Aujourdhui,
                RendezVousEnAttente = stats.EnAttente,
                RendezVousConfirmes = stats.Confirmes,
                RendezVousAnnules = stats.Annules,
                RendezVousTermines = stats.Termines,
                RendezVousCetteSemaine = stats.CetteSemaine
            };

            return View(viewModel);
        }

        // ============================================================
        // GET: RendezVous/Details/5
        // ============================================================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var rendezVou = await _context.RendezVous
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .FirstOrDefaultAsync(m => m.NumCom == id);

            if (rendezVou == null) return NotFound();

            var currentUserId = await GetCurrentUserIdAsync();

            // Vérifier l'accès pour les patients
            if (IsPatient())
            {
                if (rendezVou.PatientId != currentUserId)
                    return Forbid();
            }
            // Vérifier l'accès pour les médecins
            else if (IsMedecinOnly())
            {
                if (rendezVou.MedecinId != currentUserId)
                    return Forbid();
            }

            return View(rendezVou);
        }

        // ============================================================
        // GET: RendezVous/Create
        // ============================================================
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public IActionResult Create()
        {
            PopulateDropdowns();
            return View();
        }

        // ============================================================
        // POST: RendezVous/Create
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public async Task<IActionResult> Create([Bind("NumCom,DateHeure,Statut,MedecinId,PatientId,Motif")] RendezVou rendezVou)
        {
            ModelState.Remove("Medecin");
            ModelState.Remove("Patient");

            if (rendezVou.DateHeure <= DateTime.Now)
            {
                ModelState.AddModelError("DateHeure", "La date et l'heure doivent être supérieures à maintenant.");
            }

            var (hasConflict, conflictMessage) = await CheckRdvConflictAsync(
                rendezVou.MedecinId, rendezVou.PatientId, rendezVou.DateHeure);
            if (hasConflict)
            {
                ModelState.AddModelError("DateHeure", conflictMessage);
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Add(rendezVou);
                    await _context.SaveChangesAsync();

                    // ====== NOTIFICATIONS ======
                    var rdvComplet = await _context.RendezVous
                        .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                        .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                        .FirstOrDefaultAsync(r => r.NumCom == rendezVou.NumCom);

                    if (rdvComplet != null)
                    {
                        var patientNom = $"{rdvComplet.Patient?.IdNavigation?.Nom} {rdvComplet.Patient?.IdNavigation?.Prenom}";
                        var medecinNom = $"Dr. {rdvComplet.Medecin?.IdNavigation?.Nom}";
                        var dateRdv = rdvComplet.DateHeure.ToString("dd/MM/yyyy à HH:mm");

                        await _notificationService.CreateNotificationAsync(
                            rdvComplet.NumCom, "Confirmation",
                            $"Nouveau rendez-vous programmé le {dateRdv} avec {medecinNom}",
                            rdvComplet.PatientId);

                        await _notificationService.CreateNotificationAsync(
                            rdvComplet.NumCom, "Confirmation",
                            $"Nouveau rendez-vous programmé le {dateRdv} avec {patientNom}",
                            rdvComplet.MedecinId);

                        var currentUserId = await GetCurrentUserIdAsync();
                        if (currentUserId > 0)
                        {
                            await _notificationHelper.NotifyCreateSuccessAsync(
                                "Rendez-vous", $"RDV #{rdvComplet.NumCom} créé pour le {dateRdv}", currentUserId);
                        }
                    }

                    TempData["Success"] = "Rendez-vous créé avec succès !";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    var currentUserId = await GetCurrentUserIdAsync();
                    if (currentUserId > 0)
                    {
                        await _notificationHelper.NotifyCreateErrorAsync("Rendez-vous", ex.Message, currentUserId);
                    }
                    ModelState.AddModelError("", "Erreur lors de la création du rendez-vous.");
                }
            }

            PopulateDropdowns(rendezVou.MedecinId, rendezVou.PatientId);
            return View(rendezVou);
        }

        // ============================================================
        // GET: RendezVous/Edit/5
        // ============================================================
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var rendezVou = await _context.RendezVous.FindAsync(id);
            if (rendezVou == null) return NotFound();

            PopulateDropdowns(rendezVou.MedecinId, rendezVou.PatientId);
            return View(rendezVou);
        }

        // ============================================================
        // POST: RendezVous/Edit/5
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public async Task<IActionResult> Edit(int id, [Bind("NumCom,DateHeure,Statut,MedecinId,PatientId,Motif")] RendezVou rendezVou)
        {
            if (id != rendezVou.NumCom) return NotFound();

            ModelState.Remove("Medecin");
            ModelState.Remove("Patient");

            if (rendezVou.DateHeure <= DateTime.Now && rendezVou.Statut != "Terminé" && rendezVou.Statut != "Annulé")
            {
                ModelState.AddModelError("DateHeure", "La date et l'heure doivent être supérieures à maintenant.");
            }

            var (hasConflict, conflictMessage) = await CheckRdvConflictAsync(
                rendezVou.MedecinId, rendezVou.PatientId, rendezVou.DateHeure, id);
            if (hasConflict)
            {
                ModelState.AddModelError("DateHeure", conflictMessage);
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var ancienRdv = await _context.RendezVous
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.NumCom == id);

                    _context.Update(rendezVou);
                    await _context.SaveChangesAsync();

                    // ====== NOTIFICATIONS ======
                    var rdvComplet = await _context.RendezVous
                        .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                        .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                        .FirstOrDefaultAsync(r => r.NumCom == rendezVou.NumCom);

                    if (rdvComplet != null)
                    {
                        var dateRdv = rdvComplet.DateHeure.ToString("dd/MM/yyyy à HH:mm");

                        if (ancienRdv != null && ancienRdv.Statut != rendezVou.Statut)
                        {
                            var msgType = rendezVou.Statut == "Annulé" ? "Annulation" : "Confirmation";
                            var msg = $"Le rendez-vous du {dateRdv} a été {(rendezVou.Statut == "Annulé" ? "annulé" : rendezVou.Statut == "Confirmé" ? "confirmé" : "modifié")}";

                            await _notificationService.CreateNotificationAsync(rdvComplet.NumCom, msgType, msg, rdvComplet.PatientId);
                            await _notificationService.CreateNotificationAsync(rdvComplet.NumCom, msgType, msg, rdvComplet.MedecinId);
                        }
                        else
                        {
                            var msgModif = $"Le rendez-vous du {dateRdv} a été modifié";
                            await _notificationService.CreateNotificationAsync(rdvComplet.NumCom, "Confirmation", msgModif, rdvComplet.PatientId);
                            await _notificationService.CreateNotificationAsync(rdvComplet.NumCom, "Confirmation", msgModif, rdvComplet.MedecinId);
                        }

                        var currentUserId = await GetCurrentUserIdAsync();
                        if (currentUserId > 0)
                        {
                            await _notificationHelper.NotifyUpdateSuccessAsync("Rendez-vous", $"RDV #{rdvComplet.NumCom} modifié", currentUserId);
                        }
                    }

                    TempData["Success"] = "Rendez-vous modifié avec succès !";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!RendezVouExists(rendezVou.NumCom)) return NotFound();
                    throw;
                }
                catch (Exception ex)
                {
                    var currentUserId = await GetCurrentUserIdAsync();
                    if (currentUserId > 0)
                    {
                        await _notificationHelper.NotifyUpdateErrorAsync("Rendez-vous", ex.Message, currentUserId);
                    }
                }
                return RedirectToAction(nameof(Index));
            }

            PopulateDropdowns(rendezVou.MedecinId, rendezVou.PatientId);
            return View(rendezVou);
        }

        // ============================================================
        // GET: RendezVous/Delete/5
        // ============================================================
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var rendezVou = await _context.RendezVous
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .FirstOrDefaultAsync(m => m.NumCom == id);

            if (rendezVou == null) return NotFound();

            return View(rendezVou);
        }

        // ============================================================
        // POST: RendezVous/Delete/5
        // ============================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var rendezVou = await _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .FirstOrDefaultAsync(r => r.NumCom == id);

            if (rendezVou != null)
            {
                var dateRdv = rendezVou.DateHeure.ToString("dd/MM/yyyy à HH:mm");
                var patientId = rendezVou.PatientId;
                var medecinId = rendezVou.MedecinId;

                // Supprimer les notifications liées
                var notificationsLiees = await _context.Notifications
                    .Where(n => n.RendezVousId == id)
                    .ToListAsync();
                _context.Notifications.RemoveRange(notificationsLiees);

                _context.RendezVous.Remove(rendezVou);
                await _context.SaveChangesAsync();

                // Notifications
                var msgSuppression = $"Le rendez-vous du {dateRdv} a été supprimé";
                await _notificationService.CreateNotificationAsync(null, "Annulation", msgSuppression, patientId);
                await _notificationService.CreateNotificationAsync(null, "Annulation", msgSuppression, medecinId);

                var currentUserId = await GetCurrentUserIdAsync();
                if (currentUserId > 0)
                {
                    await _notificationHelper.NotifyDeleteSuccessAsync("Rendez-vous", $"RDV du {dateRdv}", currentUserId);
                }

                TempData["Success"] = "Rendez-vous supprimé avec succès !";
            }

            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // POST: ConfirmRdv (AJAX)
        // ============================================================
        [HttpPost]
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public async Task<IActionResult> ConfirmRdv(int id)
        {
            var rdv = await _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .FirstOrDefaultAsync(r => r.NumCom == id);

            if (rdv != null)
            {
                rdv.Statut = "Confirmé";
                await _context.SaveChangesAsync();

                var dateRdv = rdv.DateHeure.ToString("dd/MM/yyyy à HH:mm");
                var msgConfirm = $"Votre rendez-vous du {dateRdv} a été confirmé";

                await _notificationService.CreateNotificationAsync(rdv.NumCom, "Confirmation", msgConfirm, rdv.PatientId);
                await _notificationService.CreateNotificationAsync(rdv.NumCom, "Confirmation", msgConfirm, rdv.MedecinId);

                return Json(new { success = true, message = "Rendez-vous confirmé" });
            }
            return Json(new { success = false, message = "Rendez-vous non trouvé" });
        }

        // ============================================================
        // POST: CancelRdv (AJAX)
        // ============================================================
        [HttpPost]
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public async Task<IActionResult> CancelRdv(int id, string? raison)
        {
            var rdv = await _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .FirstOrDefaultAsync(r => r.NumCom == id);

            if (rdv != null)
            {
                rdv.Statut = "Annulé";
                await _context.SaveChangesAsync();

                var dateRdv = rdv.DateHeure.ToString("dd/MM/yyyy à HH:mm");
                var raisonText = string.IsNullOrEmpty(raison) ? "Non spécifiée" : raison;
                var msgAnnul = $"Le rendez-vous du {dateRdv} a été annulé. Raison : {raisonText}";

                await _notificationService.CreateNotificationAsync(rdv.NumCom, "Annulation", msgAnnul, rdv.PatientId);
                await _notificationService.CreateNotificationAsync(rdv.NumCom, "Annulation", msgAnnul, rdv.MedecinId);

                return Json(new { success = true, message = "Rendez-vous annulé" });
            }
            return Json(new { success = false, message = "Rendez-vous non trouvé" });
        }

        // ============================================================
        // POST: TerminerRdv (AJAX)
        // ============================================================
        [HttpPost]
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public async Task<IActionResult> TerminerRdv(int id)
        {
            var rdv = await _context.RendezVous.FindAsync(id);
            if (rdv != null)
            {
                rdv.Statut = "Terminé";
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Rendez-vous terminé" });
            }
            return Json(new { success = false, message = "Rendez-vous non trouvé" });
        }

        // ============================================================
        // AJAX: Vérifier conflit de RDV
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> CheckConflict(int medecinId, int patientId, DateTime dateHeure, int? excludeId = null)
        {
            var (hasConflict, message) = await CheckRdvConflictAsync(medecinId, patientId, dateHeure, excludeId);
            return Json(new { hasConflict, message });
        }

        // ============================================================
        // GET: Calendrier
        // ============================================================
        public async Task<IActionResult> Calendrier()
        {
            ViewBag.Medecins = await _context.Medecins
                .Include(m => m.IdNavigation)
                .Select(m => new { m.Id, Nom = "Dr. " + m.IdNavigation.Nom })
                .ToListAsync();

            ViewBag.IsPatient = IsPatient();
            ViewBag.IsMedecin = IsMedecinOnly();
            return View();
        }

        // ============================================================
        // GET: GetCalendarEvents (AJAX pour FullCalendar)
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> GetCalendarEvents(string start, string end, int? medecinId = null, string? status = null)
        {
            if (!DateTime.TryParse(start, out var startDate))
                startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            if (!DateTime.TryParse(end, out var endDate))
                endDate = startDate.AddMonths(2);

            var query = _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .Where(r => r.DateHeure >= startDate && r.DateHeure <= endDate);

            var currentUserId = await GetCurrentUserIdAsync();

            // Restriction pour les patients
            if (IsPatient())
            {
                query = query.Where(r => r.PatientId == currentUserId);
            }
            // Restriction pour les médecins
            else if (IsMedecinOnly())
            {
                query = query.Where(r => r.MedecinId == currentUserId);
            }

            // Filtre par médecin (uniquement pour ADMIN et SECRETAIRE)
            if (medecinId.HasValue && !IsMedecinOnly())
                query = query.Where(r => r.MedecinId == medecinId.Value);

            if (!string.IsNullOrEmpty(status))
                query = query.Where(r => r.Statut == status);

            var rendezVous = await query.ToListAsync();

            var events = rendezVous.Select(r => new
            {
                id = r.NumCom,
                title = $"{r.Patient?.IdNavigation?.Nom} {r.Patient?.IdNavigation?.Prenom?.Substring(0, Math.Min(1, r.Patient?.IdNavigation?.Prenom?.Length ?? 0))}.",
                start = r.DateHeure.ToString("yyyy-MM-ddTHH:mm:ss"),
                end = r.DateHeure.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ss"),
                color = GetStatusColor(r.Statut),
                borderColor = GetStatusBorderColor(r.Statut),
                textColor = r.Statut == "En attente" ? "#000" : "#fff",
                extendedProps = new
                {
                    patient = $"{r.Patient?.IdNavigation?.Nom} {r.Patient?.IdNavigation?.Prenom}",
                    medecin = $"Dr. {r.Medecin?.IdNavigation?.Nom} {r.Medecin?.IdNavigation?.Prenom}",
                    specialite = r.Medecin?.Specialite ?? "",
                    statut = r.Statut ?? "En attente",
                    motif = r.Motif ?? "Non spécifié"
                }
            }).ToList();

            return Json(events);
        }

        private string GetStatusColor(string? statut) => statut switch
        {
            "Confirmé" => "#28a745",
            "En attente" => "#ffc107",
            "Annulé" => "#dc3545",
            "Terminé" => "#17a2b8",
            _ => "#6c757d"
        };

        private string GetStatusBorderColor(string? statut) => statut switch
        {
            "Confirmé" => "#1e7b34",
            "En attente" => "#d39e00",
            "Annulé" => "#bd2130",
            "Terminé" => "#117a8b",
            _ => "#545b62"
        };

        // ============================================================
        // GET: ExportExcel
        // ============================================================
        [Authorize(Roles = "ADMIN,MEDECIN,SECRETAIRE")]
        public async Task<IActionResult> ExportExcel(
            string? searchTerm,
            string? statut,
            string? periode,
            DateTime? dateDebut,
            DateTime? dateFin,
            int? medecinId)
        {
            var query = _context.RendezVous
                .Include(r => r.Patient).ThenInclude(p => p.IdNavigation)
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .AsQueryable();

            // Appliquer restriction pour médecins
            if (IsMedecinOnly())
            {
                var currentUserId = await GetCurrentUserIdAsync();
                query = query.Where(r => r.MedecinId == currentUserId);
            }

            var today = DateTime.Today;

            if (!string.IsNullOrEmpty(periode))
            {
                switch (periode)
                {
                    case "aujourd'hui":
                        query = query.Where(r => r.DateHeure.Date == today);
                        break;
                    case "semaine":
                        var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                        var endOfWeek = startOfWeek.AddDays(7);
                        query = query.Where(r => r.DateHeure >= startOfWeek && r.DateHeure < endOfWeek);
                        break;
                    case "mois":
                        var startOfMonth = new DateTime(today.Year, today.Month, 1);
                        var endOfMonth = startOfMonth.AddMonths(1);
                        query = query.Where(r => r.DateHeure >= startOfMonth && r.DateHeure < endOfMonth);
                        break;
                }
            }

            if (!string.IsNullOrEmpty(searchTerm))
            {
                var search = searchTerm.ToLower();
                query = query.Where(r =>
                    r.Patient.IdNavigation.Nom.ToLower().Contains(search) ||
                    r.Patient.IdNavigation.Prenom.ToLower().Contains(search) ||
                    r.Medecin.IdNavigation.Nom.ToLower().Contains(search));
            }

            if (!string.IsNullOrEmpty(statut))
                query = query.Where(r => r.Statut == statut);

            if (dateDebut.HasValue)
                query = query.Where(r => r.DateHeure.Date >= dateDebut.Value.Date);

            if (dateFin.HasValue)
                query = query.Where(r => r.DateHeure.Date <= dateFin.Value.Date);

            if (medecinId.HasValue && !IsMedecinOnly())
                query = query.Where(r => r.MedecinId == medecinId.Value);

            var rdvs = await query.OrderBy(r => r.DateHeure).ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Rendez-vous");

            worksheet.Cell(1, 1).Value = "Liste des Rendez-vous";
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 16;
            worksheet.Range(1, 1, 1, 6).Merge();

            worksheet.Cell(2, 1).Value = $"Exporté le {DateTime.Now:dd/MM/yyyy à HH:mm}";
            worksheet.Cell(2, 1).Style.Font.Italic = true;
            worksheet.Range(2, 1, 2, 6).Merge();

            var headerRow = 4;
            var headers = new[] { "N° RDV", "Date & Heure", "Patient", "Médecin", "Spécialité", "Statut" };
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(headerRow, i + 1).Value = headers[i];
            }

            var headerRange = worksheet.Range(headerRow, 1, headerRow, 6);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#667eea");
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var currentRow = headerRow + 1;
            foreach (var rdv in rdvs)
            {
                worksheet.Cell(currentRow, 1).Value = rdv.NumCom;
                worksheet.Cell(currentRow, 2).Value = rdv.DateHeure.ToString("dd/MM/yyyy HH:mm");
                worksheet.Cell(currentRow, 3).Value = $"{rdv.Patient?.IdNavigation?.Nom} {rdv.Patient?.IdNavigation?.Prenom}";
                worksheet.Cell(currentRow, 4).Value = $"Dr. {rdv.Medecin?.IdNavigation?.Nom}";
                worksheet.Cell(currentRow, 5).Value = rdv.Medecin?.Specialite ?? "-";
                worksheet.Cell(currentRow, 6).Value = rdv.Statut;

                worksheet.Cell(currentRow, 6).Style.Fill.BackgroundColor = rdv.Statut switch
                {
                    "Confirmé" => XLColor.FromHtml("#d4edda"),
                    "En attente" => XLColor.FromHtml("#fff3cd"),
                    "Annulé" => XLColor.FromHtml("#f8d7da"),
                    "Terminé" => XLColor.FromHtml("#d1ecf1"),
                    _ => XLColor.White
                };
                currentRow++;
            }

            worksheet.Columns().AdjustToContents();

            currentRow += 2;
            worksheet.Cell(currentRow, 1).Value = "Statistiques";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            currentRow++;
            worksheet.Cell(currentRow, 1).Value = $"Total : {rdvs.Count} rendez-vous";
            currentRow++;
            worksheet.Cell(currentRow, 1).Value = $"Confirmés : {rdvs.Count(r => r.Statut == "Confirmé")}";
            currentRow++;
            worksheet.Cell(currentRow, 1).Value = $"En attente : {rdvs.Count(r => r.Statut == "En attente")}";
            currentRow++;
            worksheet.Cell(currentRow, 1).Value = $"Annulés : {rdvs.Count(r => r.Statut == "Annulé")}";

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"RendezVous_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        #region ================ ESPACE PATIENT ================

        [Authorize(Roles = "PATIENT")]
        public async Task<IActionResult> MesRendezVous(string? filtre)
        {
            var patientId = await GetCurrentUserIdAsync();
            if (patientId == 0) return RedirectToAction("Login", "Account");

            var query = _context.RendezVous
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .Where(r => r.PatientId == patientId);

            ViewBag.Filtre = filtre ?? "tous";
            switch (filtre)
            {
                case "avenir":
                    query = query.Where(r => r.DateHeure >= DateTime.Now && r.Statut != "Annulé");
                    break;
                case "attente":
                    query = query.Where(r => r.Statut == "En attente");
                    break;
                case "confirme":
                    query = query.Where(r => r.Statut == "Confirmé");
                    break;
                case "passe":
                    query = query.Where(r => r.DateHeure < DateTime.Now || r.Statut == "Terminé");
                    break;
            }

            var mesRdv = await query.OrderByDescending(r => r.DateHeure).ToListAsync();

            var tousRdv = await _context.RendezVous.Where(r => r.PatientId == patientId).ToListAsync();
            ViewBag.MesRdv = mesRdv;
            ViewBag.TotalRdv = tousRdv.Count;
            ViewBag.EnAttente = tousRdv.Count(r => r.Statut == "En attente");
            ViewBag.Confirmes = tousRdv.Count(r => r.Statut == "Confirmé");
            ViewBag.Termines = tousRdv.Count(r => r.Statut == "Terminé");

            ViewBag.ProchainRdv = await _context.RendezVous
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .Where(r => r.PatientId == patientId && r.DateHeure >= DateTime.Now && r.Statut != "Annulé")
                .OrderBy(r => r.DateHeure)
                .FirstOrDefaultAsync();

            return View();
        }

        [Authorize(Roles = "PATIENT")]
        public async Task<IActionResult> DemanderRendezVous()
        {
            ViewBag.Medecins = await _context.Medecins
                .Include(m => m.IdNavigation)
                .Select(m => new { m.Id, Nom = m.IdNavigation.Nom, Prenom = m.IdNavigation.Prenom, m.Specialite })
                .ToListAsync();

            return View(new RendezVou());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "PATIENT")]
        public async Task<IActionResult> DemanderRendezVous(RendezVou model)
        {
            var patientId = await GetCurrentUserIdAsync();
            if (patientId == 0) return RedirectToAction("Login", "Account");

            model.PatientId = patientId;
            model.Statut = "En attente";

            if (model.DateHeure <= DateTime.Now)
                ModelState.AddModelError("DateHeure", "La date doit être dans le futur.");

            var (hasConflict, msg) = await CheckRdvConflictAsync(model.MedecinId, patientId, model.DateHeure);
            if (hasConflict)
                ModelState.AddModelError("DateHeure", msg);

            ModelState.Remove("PatientId");
            ModelState.Remove("Statut");
            ModelState.Remove("Medecin");
            ModelState.Remove("Patient");

            if (ModelState.IsValid)
            {
                _context.Add(model);
                await _context.SaveChangesAsync();

                var rdvComplet = await _context.RendezVous
                    .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                    .FirstOrDefaultAsync(r => r.NumCom == model.NumCom);

                if (rdvComplet != null)
                {
                    var dateStr = rdvComplet.DateHeure.ToString("dd/MM/yyyy à HH:mm");
                    await _notificationService.CreateNotificationAsync(rdvComplet.NumCom, "Confirmation",
                        $"Nouvelle demande de RDV le {dateStr}", rdvComplet.MedecinId);
                }

                TempData["Success"] = "Votre demande de rendez-vous a été envoyée !";
                return RedirectToAction(nameof(MesRendezVous));
            }

            ViewBag.Medecins = await _context.Medecins
                .Include(m => m.IdNavigation)
                .Select(m => new { m.Id, Nom = m.IdNavigation.Nom, Prenom = m.IdNavigation.Prenom, m.Specialite })
                .ToListAsync();

            return View(model);
        }

        [Authorize(Roles = "PATIENT")]
        public async Task<IActionResult> ModifierMonRdv(int? id)
        {
            if (id == null) return NotFound();

            var patientId = await GetCurrentUserIdAsync();
            var rdv = await _context.RendezVous
                .Include(r => r.Medecin).ThenInclude(m => m.IdNavigation)
                .FirstOrDefaultAsync(r => r.NumCom == id && r.PatientId == patientId);

            if (rdv == null) return NotFound();

            if (rdv.DateHeure < DateTime.Now || rdv.Statut == "Annulé" || rdv.Statut == "Terminé")
            {
                TempData["Error"] = "Ce rendez-vous ne peut plus être modifié.";
                return RedirectToAction(nameof(MesRendezVous));
            }

            return View(rdv);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "PATIENT")]
        public async Task<IActionResult> ModifierMonRdv(int id, RendezVou model)
        {
            if (id != model.NumCom) return NotFound();

            var patientId = await GetCurrentUserIdAsync();
            var rdvOriginal = await _context.RendezVous.AsNoTracking()
                .FirstOrDefaultAsync(r => r.NumCom == id && r.PatientId == patientId);

            if (rdvOriginal == null) return NotFound();

            if (model.DateHeure <= DateTime.Now)
                ModelState.AddModelError("DateHeure", "La date doit être dans le futur.");

            ModelState.Remove("Medecin");
            ModelState.Remove("Patient");

            if (ModelState.IsValid)
            {
                model.PatientId = patientId;
                model.MedecinId = rdvOriginal.MedecinId;

                _context.Update(model);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Votre rendez-vous a été modifié !";
                return RedirectToAction(nameof(MesRendezVous));
            }

            model.Medecin = await _context.Medecins
                .Include(m => m.IdNavigation)
                .FirstOrDefaultAsync(m => m.Id == model.MedecinId);

            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = "PATIENT")]
        public async Task<IActionResult> ConfirmerMonRdv(int id)
        {
            var patientId = await GetCurrentUserIdAsync();
            var rdv = await _context.RendezVous.FirstOrDefaultAsync(r => r.NumCom == id && r.PatientId == patientId);

            if (rdv == null)
                return Json(new { success = false, message = "Rendez-vous non trouvé" });

            if (rdv.DateHeure < DateTime.Now)
                return Json(new { success = false, message = "Ce rendez-vous est déjà passé" });

            if (rdv.Statut == "Confirmé")
                return Json(new { success = false, message = "Déjà confirmé" });

            if (rdv.Statut == "Annulé")
                return Json(new { success = false, message = "Ce rendez-vous a été annulé" });

            rdv.Statut = "Confirmé";
            await _context.SaveChangesAsync();

            var dateStr = rdv.DateHeure.ToString("dd/MM/yyyy à HH:mm");
            await _notificationService.CreateNotificationAsync(rdv.NumCom, "Confirmation",
                $"Le patient a confirmé sa présence pour le {dateStr}", rdv.MedecinId);

            return Json(new { success = true, message = "Votre présence a été confirmée !" });
        }

        [HttpPost]
        [Authorize(Roles = "PATIENT")]
        public async Task<IActionResult> AnnulerMonRdv(int id, string? motif)
        {
            var patientId = await GetCurrentUserIdAsync();
            var rdv = await _context.RendezVous.FirstOrDefaultAsync(r => r.NumCom == id && r.PatientId == patientId);

            if (rdv == null)
                return Json(new { success = false, message = "Rendez-vous non trouvé" });

            if (rdv.DateHeure < DateTime.Now)
                return Json(new { success = false, message = "Ce rendez-vous est déjà passé" });

            if (rdv.Statut == "Annulé")
                return Json(new { success = false, message = "Déjà annulé" });

            if (rdv.Statut == "Terminé")
                return Json(new { success = false, message = "Ce rendez-vous est terminé" });

            rdv.Statut = "Annulé";
            if (!string.IsNullOrEmpty(motif))
                rdv.Motif = (rdv.Motif ?? "") + $" [Annulé par patient: {motif}]";

            await _context.SaveChangesAsync();

            var dateStr = rdv.DateHeure.ToString("dd/MM/yyyy à HH:mm");
            var raisonTxt = string.IsNullOrEmpty(motif) ? "" : $" Raison: {motif}";
            await _notificationService.CreateNotificationAsync(rdv.NumCom, "Annulation",
                $"Le patient a annulé le RDV du {dateStr}.{raisonTxt}", rdv.MedecinId);

            return Json(new { success = true, message = "Votre rendez-vous a été annulé" });
        }

        #endregion

        // ============================================================
        // HELPERS
        // ============================================================
        private bool RendezVouExists(int id)
        {
            return _context.RendezVous.Any(e => e.NumCom == id);
        }

        private void PopulateDropdowns(object? selectedMedecin = null, object? selectedPatient = null)
        {
            var medecinsQuery = _context.Medecins
                .Include(m => m.IdNavigation)
                .Select(m => new
                {
                    Id = m.Id,
                    NomComplet = "Dr. " + m.IdNavigation.Nom + " " + m.IdNavigation.Prenom + " (" + m.Specialite + ")"
                }).ToList();

            var patientsQuery = _context.Patients
                .Include(p => p.IdNavigation)
                .Select(p => new
                {
                    Id = p.Id,
                    NomComplet = p.IdNavigation.Nom + " " + p.IdNavigation.Prenom
                }).ToList();

            ViewData["MedecinId"] = new SelectList(medecinsQuery, "Id", "NomComplet", selectedMedecin);
            ViewData["PatientId"] = new SelectList(patientsQuery, "Id", "NomComplet", selectedPatient);
        }
    }
}