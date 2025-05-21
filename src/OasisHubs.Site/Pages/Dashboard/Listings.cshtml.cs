using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OasisHubs.DbModels;

namespace OasisHubs.Site.Pages.Dashboard;

public class Listings : PageModel {
   private readonly UserManager<OasisHubsUser> _userManager;
   private readonly ILogger<Listings> _logger;
   private readonly OasisHubsDbContext _dbContext;

   public OasisHubsUser? OasisUser { get; set; }
   public IEnumerable<HubRental> Rentals { get; set; } = [];

   public Listings(UserManager<OasisHubsUser> userManager,
      OasisHubsDbContext dbContext, ILogger<Listings> logger) {
      this._dbContext = dbContext;
      this._userManager = userManager;
      this._logger = logger;
   }

   public async Task<IActionResult> OnGetAsync() {
      OasisUser = await _userManager.GetUserAsync(HttpContext.User);

      if (OasisUser is null) {
         this._logger.LogCritical("Host user record not found");
         return RedirectToPage("/Index");
      }

      Rentals = _dbContext.HubRentals
         .Where(h => h.StripeAccountId == OasisUser.StripeAccountId).ToList();

      return Page();
   }

   public async Task<IActionResult> OnPostAddListingAsync() {
      OasisUser = await _userManager.GetUserAsync(HttpContext.User);

      if (OasisUser is null) {
         this._logger.LogCritical("Host user record not found");
         return RedirectToPage("/Index");
      }

      var newRental = new HubRental {
         Title = Request.Form["name"].ToString(),
         Description = Request.Form["description"].ToString(),
         ImageUrl =  Request.Form["image"].ToString(),
         Capacity = int.Parse(Request.Form["capacity"].ToString()),
         HubTier  = Enum.Parse<RentalTier>(Request.Form["tier"].ToString()),
         HubType = Enum.Parse<RentalType>(Request.Form["type"].ToString()),
         Location = Request.Form["location"].ToString(),
         StripeAccountId = OasisUser.StripeAccountId,
         ReferenceCode = ReferenceCodeGenerator.GetUniqueKey()
      };

      _logger.LogInformation("Creating stripe product for {ProductName}", newRental.Title);

      // Save a product in the database
      _dbContext.HubRentals.Add(newRental);
      await _dbContext.SaveChangesAsync();
      _logger.LogInformation("HubRental {ProductName} created in database", newRental.Title);

      return RedirectToPage("/dashboard/listings");
   }

   public async Task<IActionResult> OnPostDeactivateListingAsync()
      => await UpdateStatusAndReturn(false);

   public async Task<IActionResult> OnPostActivateListingAsync()
      => await UpdateStatusAndReturn(true);


   private async Task<IActionResult> UpdateStatusAndReturn(bool activate) {
      var referenceCode = Request.Form["referenceCode"].ToString();
      if (!string.IsNullOrEmpty(referenceCode)) {
         var rental = _dbContext.HubRentals.FirstOrDefault(
            h => h.ReferenceCode.ToUpper() == referenceCode.ToUpper());

         if (rental != null) {
            rental.IsActive = activate;
            await _dbContext.SaveChangesAsync();
            this._logger.LogInformation("Product ({ProductID}) has been deactivated", rental.Title);
         }
      }

      return RedirectToPage("/dashboard/listings");
   }
}
