using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OasisHubs.DbModels;

namespace OasisHubs.Site.Pages;
[Authorize(Policy = "can_view_listings")]
public class ListingsModel(OasisHubsDbContext dbContext) : PageModel {
   public IEnumerable<string> ListingCategories { get; init; } = new[] {
      "Apartment", "House", "Treehouse", "Mansion", "Boats", "Tiny Homes",
      "Mobile Home","Beachfront"
   };

   public IEnumerable<HubRental> Rentals { get; set; } = Enumerable.Empty<HubRental>();

   public async Task<IActionResult> OnGet() {
      Rentals = await dbContext.HubRentals.Where(h => h.IsActive).ToListAsync();
      return Page();
   }
}

