using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SignService.Services;

namespace SignService.Controllers
{
    [Authorize(Roles = "admin_signservice")]
    [Authorize]
    [RequireHttps]
    public class AdminController : Controller
    {
        readonly IAdminService adminService;

        public AdminController(IAdminService adminService)
        {
            this.adminService = adminService;
        }
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult RegisterExtensionAttributes()
        {
            // Do something

            return RedirectToAction(nameof(Index));
        }

        public IActionResult UnRegisterExtensionAttributes()
        {
            // Do something

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Users()
        {
            var users = await adminService.GetUsersAsync();

            return View(users);
        }
    }
}