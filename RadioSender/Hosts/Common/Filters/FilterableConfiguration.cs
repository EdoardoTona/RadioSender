﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioSender.Hosts.Common.Filters
{
  public record FilterableConfiguration
  {
    public string Filter { get; set; }
  }
}