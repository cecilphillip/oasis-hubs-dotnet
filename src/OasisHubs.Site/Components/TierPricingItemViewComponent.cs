using Microsoft.AspNetCore.Mvc;

using Stripe;

namespace OasisHubs.Site.Components;

[ViewComponent]
public class TierPricingItemViewComponent : ViewComponent
{
   public IViewComponentResult Invoke(Product tierProduct) {
      return View(new TierPricingItemModel(tierProduct));
   }

   public record TierPricingItemModel(Product TierProduct);
}
