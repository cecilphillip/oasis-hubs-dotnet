using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using Stripe;

namespace OasisHubs.Site.Pages;

[Authorize(Policy = "can_view_listings")]
public class ListingDetailModel : PageModel {
   private readonly OasisHubsDbContext _dbContext;
   private readonly UserManager<OasisHubsUser> _userManager;
   private readonly StripeClient _stripeClient;
   private readonly ILogger<ListingDetailModel> _logger;
   private readonly Channel<HubUsageReport> _usageReportChannel;

   [BindProperty(SupportsGet = true)] public string ReferenceCode { get; set; } = string.Empty;
   public HubRental? Rental { get; set; }

   public OasisHubsUser? OasisUser { get; set; }

   public ListingDetailModel(OasisHubsDbContext dbContext, Channel<HubUsageReport> usageReportChannel,
      UserManager<OasisHubsUser> userManager, StripeClient stripeClient, ILogger<ListingDetailModel> logger) {
      this._dbContext = dbContext;
      this._usageReportChannel = usageReportChannel;
      this._userManager = userManager;
      this._stripeClient = stripeClient;
      this._logger = logger;
   }

   public async Task<IActionResult> OnGetAsync() {
      if (string.IsNullOrEmpty(ReferenceCode)) return RedirectToPage("/listings");

      OasisUser = await _userManager.GetUserAsync(HttpContext.User);

      Rental = this._dbContext.HubRentals
         .FirstOrDefault(h => h.IsActive && h.ReferenceCode.ToUpper() == ReferenceCode.ToUpper());

      if (Rental == null)
         return RedirectToPage("/listings");

      return Page();
   }

   public async Task<IActionResult> OnPostAsync() {
      OasisUser = await _userManager.GetUserAsync(HttpContext.User);

      var renterId = OasisUser!.Id;
      if (string.IsNullOrEmpty(renterId)) {
         return RedirectToPage("/signin");
      }

      var checkInDate = DateTimeOffset.Parse(Request.Form["checkInDate"].ToString());

      var hours = int.Parse(Request.Form["hours"].ToString());
      var booking = new Booking {
         RenterId = renterId,
         RentalId = Request.Form["rentalId"].ToString(),
         Hours = hours,
         ReservedDateUtc = checkInDate.ToUniversalTime()
      };

      this._dbContext.Bookings.Add(booking);
      await this._dbContext.SaveChangesAsync();

      // retrieve subscription
      var subscription = await this._stripeClient.V1.Subscriptions.GetAsync(OasisUser.ActiveSubscriptionId);
      if (subscription is not null) {
         
         await _usageReportChannel.Writer.WriteAsync(new HubUsageReport(OasisUser.StripeCustomerId, hours));

         return RedirectToPage("/bookings");
      }

      this._logger.LogError("Subscription not found {SubscriptionId}", OasisUser.ActiveSubscriptionId);
      return Page();
   }
}
