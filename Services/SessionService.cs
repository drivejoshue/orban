using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrbanaDrive.Services;

public class SessionService
{
    public ApiService.MeDto? Me { get; private set; }

    public void Set(ApiService.MeDto? me) => Me = me;

    public bool HasOpenShift =>
        Me?.current_shift != null && string.Equals(Me.current_shift.status, "abierto", StringComparison.OrdinalIgnoreCase);
}
