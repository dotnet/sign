﻿using System;

namespace SignService.Models
{
    public class VaultModel
    {
        public Uri VaultUri { get; set; }
        public string Location { get; set; }
        public string Name { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
    }
}
