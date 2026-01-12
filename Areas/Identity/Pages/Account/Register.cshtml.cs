// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using GestionCabinetMedical.Areas.Identity.Data;
using GestionCabinetMedical.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

namespace GestionCabinetMedical.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<Userper> _signInManager;
        private readonly UserManager<Userper> _userManager;
        private readonly IUserStore<Userper> _userStore;
        private readonly IUserEmailStore<Userper> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly BdCabinetMedicalContext _context;
        public RegisterModel(
            UserManager<Userper> userManager,
            IUserStore<Userper> userStore,
            SignInManager<Userper> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            BdCabinetMedicalContext context
            )
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _context = context;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Required]
            [StringLength(100)]
            [Display(Name = "Nom")]
            public string Nom { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "Le {0} doit comporter au moins {2} et au maximum {1} caractères.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "Mot de passe")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirmer le mot de passe")]
            [Compare("Password", ErrorMessage = "Les mots de passe ne correspondent pas.")]
            public string ConfirmPassword { get; set; }
        }


        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                // ============================================================
                // ÉTAPE 1 : Créer l'utilisateur dans ASP.NET Identity
                // ============================================================
                var user = new Userper
                {
                    UserName = Input.Email,
                    Email = Input.Email,
                    Nom = Input.Nom,
                    EmailConfirmed = true
                };

                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    // ============================================================
                    // ÉTAPE 2 : Assigner le rôle USER par défaut ✅
                    // ============================================================
                    await _userManager.AddToRoleAsync(user, "USER");

                    // ============================================================
                    // ÉTAPE 3 : Créer l'utilisateur dans la table métier
                    // ============================================================
                    using (var scope = HttpContext.RequestServices.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<BdCabinetMedicalContext>();

                        var utilisateur = new Utilisateur
                        {
                            Nom = Input.Nom,
                            Prenom = Input.Nom,  // ou Input.Prenom si vous l'ajoutez
                            Email = Input.Email,
                            MotDePasse = "***",  // Géré par Identity
                            Telephone = "",
                            EstActif = true
                        };

                        dbContext.Utilisateurs.Add(utilisateur);
                        await dbContext.SaveChangesAsync();

                        // ============================================================
                        // ÉTAPE 4 : Créer le patient lié (optionnel)
                        // ============================================================
                        var patient = new Patient
                        {
                            Id = utilisateur.Id,
                            NumSecuriteSociale = "TEMP-" + utilisateur.Id,
                            DateNaissance = null
                        };

                        dbContext.Patients.Add(patient);
                        await dbContext.SaveChangesAsync();
                    }

                    TempData["Success"] = "Compte créé avec succès ! Vous pouvez maintenant vous connecter.";
                    return LocalRedirect("~/Identity/Account/Login");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }

        private Userper CreateUser()
        {
            try
            {
                return Activator.CreateInstance<Userper>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(Userper)}'. " +
                    $"Ensure that '{nameof(Userper)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the external login page in /Areas/Identity/Pages/Account/ExternalLogin.cshtml");
            }
        }

        private IUserEmailStore<Userper> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<Userper>)_userStore;
        }
    }
}
