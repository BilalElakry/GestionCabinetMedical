using Microsoft.EntityFrameworkCore;
using GestionCabinetMedical.Models;
using Microsoft.AspNetCore.Identity;
using GestionCabinetMedical.Areas.Identity.Data;
using GestionCabinetMedical.Services;
using GestionCabinetMedical.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configuration DbContext
builder.Services.AddDbContext<BdCabinetMedicalContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CabinetMedicalConnection")));

// Configuration Identity
builder.Services.AddDbContext<CabinetMedicalIdentityContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CabinetMedicalConnection")));

builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ICrudNotificationHelper, CrudNotificationHelper>();
// Services
builder.Services.AddScoped<IChatbotService, ChatbotService>();
builder.Services.AddIdentity<Userper, IdentityRole>(options => {
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
})
.AddEntityFrameworkStores<CabinetMedicalIdentityContext>()
.AddDefaultUI()
.AddDefaultTokenProviders();

builder.Services.AddRazorPages();

var app = builder.Build();




// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();