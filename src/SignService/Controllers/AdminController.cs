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

        public async Task<IActionResult> Users()
        {
            var users = await adminService.GetConfiguredUsersAsync();
           // var users = await adminService.GetUsersAsync();

            return View(users);
        }

        public async Task<IActionResult> Search(string displayName)
        {
            var users = await adminService.GetUsersAsync(displayName);

            return View(users);
        }
    }
}