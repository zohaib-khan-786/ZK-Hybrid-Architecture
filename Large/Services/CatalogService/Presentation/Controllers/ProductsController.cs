using Microsoft.AspNetCore.Mvc;

namespace CatalogService.Presentation.Controllers
{
    public class ProductsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
