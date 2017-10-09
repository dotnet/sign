using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SignService.Models;
using SignService.Services;

namespace SignService.Controllers
{
    public class KeyVaultController : Controller
    {
        readonly IKeyVaultAdminService keyVaultAdminService;

        public KeyVaultController(IKeyVaultAdminService keyVaultAdminService)
        {
            this.keyVaultAdminService = keyVaultAdminService;
        }
        // GET: KeyVault
        public async Task<IActionResult> Index()
        {
            var vaults = await keyVaultAdminService.ListKeyVaultsAsync();
            
            return View(vaults);
        }

        // GET: KeyVault/Details/5
        public async Task<IActionResult> Details(string id)
        {
            try
            {

                var vault = await keyVaultAdminService.GetVaultAsync(id);
                var certificates = await keyVaultAdminService.GetCertificatesInVaultAsync(vault.VaultUri);

                var model = new KeyVaultDetailsModel
                {
                    Vault = vault,
                    CertificateModels = certificates
                };

                return View(model);
            }
            catch (Exception e)
            {
                ViewBag.Id = id;
                return View("Details.Error");
            }
        }
        
        // GET: KeyVault/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: KeyVault/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                // TODO: Add update logic here

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}