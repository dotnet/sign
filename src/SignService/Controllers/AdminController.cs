using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignService.Services;

namespace SignService.Controllers
{
    [Authorize(Roles = "admin_signservice")]
    [RequireHttps]
    public class AdminController : Controller
    {
        readonly IUserAdminService adminService;

        public AdminController(IUserAdminService adminService)
        {
            this.adminService = adminService;
        }
        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> RegisterExtensionAttributes()
        {
            await adminService.RegisterExtensionPropertiesAsync();
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> UnRegisterExtensionAttributes()
        {
            await adminService.UnRegisterExtensionPropertiesAsync();
            return RedirectToAction(nameof(Index));
        }


    }
}