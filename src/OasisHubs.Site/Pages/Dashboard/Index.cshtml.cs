using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OasisHubs.DbModels;

namespace OasisHubs.Site.Pages.Dashboard;

public class Index : PageModel {
   private readonly OasisHubsDbContext _dbContext;
   private readonly UserManager<OasisHubsUser> _userManager;
   private readonly ILogger<Index> _logger;

   public IEnumerable<Booking> GuestBookings { get; set; } = default!;
   public Index(UserManager<OasisHubsUser> userManager,ILogger<Index> logger, OasisHubsDbContext dbContext) {
      this._userManager = userManager;
      this._logger = logger;
      this._dbContext = dbContext;
   }
   public async Task<IActionResult> OnGetAsync() {
      var user = await this._userManager.GetUserAsync(User);

      if (user == null) {
         this._logger.LogError("User is null");
         return RedirectToPage("Error");
      }

      GuestBookings = await this._dbContext.Bookings
         .Where(b => b.Rental.StripeAccountId == user.StripeAccountId)
         .Include(b => b.Rental)
         .Include(b => b.Renter)
      .ToListAsync();

      return Page();
   }
}
