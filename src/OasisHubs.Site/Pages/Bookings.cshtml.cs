using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OasisHubs.DbModels;

namespace OasisHubs.Site.Pages;

[Authorize]
public class Bookings : PageModel {
   private readonly OasisHubsDbContext _dbContext;
   private readonly UserManager<OasisHubsUser> _userManager;
   private readonly ILogger<Bookings> _logger;

   public IEnumerable<Booking> UserBookings { get; set; } = default!;

   public Bookings(OasisHubsDbContext dbContext,
      UserManager<OasisHubsUser> userManager, ILogger<Bookings> logger) {

      this._dbContext = dbContext;
      this._userManager = userManager;
      this._logger = logger;
   }

   public async Task<IActionResult> OnGet() {
      var user = await _userManager.GetUserAsync(HttpContext.User);

      if (user == null) {
         this._logger.LogError("User is null");
         return RedirectToPage("Error");
      }

      UserBookings = await _dbContext.Bookings
         .Include(b => b.Rental)
         .Include(b => b.Renter)
         .Where(b => b.RenterId == user.Id).ToListAsync();
      return Page();
   }
}
