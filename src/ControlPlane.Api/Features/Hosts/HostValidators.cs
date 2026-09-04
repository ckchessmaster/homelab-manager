using System.Net;
using System.Text.RegularExpressions;

namespace ControlPlane.Api.Features.Hosts;

public static partial class HostValidators
{
    private static readonly HashSet<string> AllowedOsFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "linux_debian",
        "linux_ubuntu",
        "linux_rhel",
        "linux_rocky",
        "linux_fedora",
        "linux_arch",
        "linux_alpine",
        "linux_suse",
        "windows",
        "freebsd"
    };

    private static readonly HashSet<string> AllowedTargetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "baremetal",
        "proxmox_vm",
        "proxmox_lxc"
    };

    private static readonly Regex HostnameRegex = new(
        @"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MacAddressRegex = new(
        @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IDictionary<string, string[]> ValidateCreate(CreateHostRequest req)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void AddError(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                list = new List<string>();
                errors[field] = list;
            }
            list.Add(message);
        }

        // Hostname
        if (string.IsNullOrWhiteSpace(req.Hostname))
        {
            AddError(nameof(req.Hostname), "Hostname is required.");
        }
        else if (req.Hostname.Length > 253 || !HostnameRegex.IsMatch(req.Hostname))
        {
            AddError(nameof(req.Hostname), "Hostname must be a valid DNS hostname (alphanumeric with hyphens and dots).");
        }

        // IP Address
        if (string.IsNullOrWhiteSpace(req.IpAddress))
        {
            AddError(nameof(req.IpAddress), "IP address is required.");
        }
        else if (!IPAddress.TryParse(req.IpAddress, out _))
        {
            AddError(nameof(req.IpAddress), "IP address must be a valid IPv4 or IPv6 address.");
        }

        // OS Family
        if (string.IsNullOrWhiteSpace(req.OsFamily))
        {
            AddError(nameof(req.OsFamily), "OS family is required.");
        }
        else if (!AllowedOsFamilies.Contains(req.OsFamily))
        {
            AddError(nameof(req.OsFamily), $"OS family must be one of: {string.Join(", ", AllowedOsFamilies)}.");
        }

        // Target Type
        if (string.IsNullOrWhiteSpace(req.TargetType))
        {
            AddError(nameof(req.TargetType), "Target type is required.");
        }
        else if (!AllowedTargetTypes.Contains(req.TargetType))
        {
            AddError(nameof(req.TargetType), $"Target type must be one of: {string.Join(", ", AllowedTargetTypes)}.");
        }

        // Proxmox Correlation validation
        if (req.ProxmoxVmid.HasValue && req.ProxmoxVmid.Value <= 0)
        {
            AddError(nameof(req.ProxmoxVmid), "Proxmox VMID must be a positive integer.");
        }

        // iDRAC IP
        if (!string.IsNullOrWhiteSpace(req.IdracIp) && !IPAddress.TryParse(req.IdracIp, out _))
        {
            AddError(nameof(req.IdracIp), "iDRAC IP address must be a valid IPv4 or IPv6 address.");
        }

        // UniFi Switch MAC
        if (!string.IsNullOrWhiteSpace(req.UnifiSwitchMac) && !MacAddressRegex.IsMatch(req.UnifiSwitchMac))
        {
            AddError(nameof(req.UnifiSwitchMac), "UniFi Switch MAC must be a valid MAC address (e.g. 00:11:22:33:44:55).");
        }

        // UniFi Switch Port
        if (req.UnifiSwitchPort.HasValue && (req.UnifiSwitchPort.Value <= 0 || req.UnifiSwitchPort.Value > 65535))
        {
            AddError(nameof(req.UnifiSwitchPort), "UniFi Switch Port must be between 1 and 65535.");
        }

        return errors.ToDictionary(k => k.Key, v => v.Value.ToArray());
    }

    public static IDictionary<string, string[]> ValidateUpdate(UpdateHostRequest req)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void AddError(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                list = new List<string>();
                errors[field] = list;
            }
            list.Add(message);
        }

        if (req.Hostname != null)
        {
            if (string.IsNullOrWhiteSpace(req.Hostname))
            {
                AddError(nameof(req.Hostname), "Hostname cannot be empty.");
            }
            else if (req.Hostname.Length > 253 || !HostnameRegex.IsMatch(req.Hostname))
            {
                AddError(nameof(req.Hostname), "Hostname must be a valid DNS hostname.");
            }
        }

        if (req.IpAddress != null)
        {
            if (string.IsNullOrWhiteSpace(req.IpAddress))
            {
                AddError(nameof(req.IpAddress), "IP address cannot be empty.");
            }
            else if (!IPAddress.TryParse(req.IpAddress, out _))
            {
                AddError(nameof(req.IpAddress), "IP address must be a valid IPv4 or IPv6 address.");
            }
        }

        if (req.OsFamily != null && !AllowedOsFamilies.Contains(req.OsFamily))
        {
            AddError(nameof(req.OsFamily), $"OS family must be one of: {string.Join(", ", AllowedOsFamilies)}.");
        }

        if (req.TargetType != null && !AllowedTargetTypes.Contains(req.TargetType))
        {
            AddError(nameof(req.TargetType), $"Target type must be one of: {string.Join(", ", AllowedTargetTypes)}.");
        }

        if (req.ProxmoxVmid.HasValue && req.ProxmoxVmid.Value <= 0)
        {
            AddError(nameof(req.ProxmoxVmid), "Proxmox VMID must be a positive integer.");
        }

        if (!string.IsNullOrWhiteSpace(req.IdracIp) && !IPAddress.TryParse(req.IdracIp, out _))
        {
            AddError(nameof(req.IdracIp), "iDRAC IP address must be a valid IPv4 or IPv6 address.");
        }

        if (!string.IsNullOrWhiteSpace(req.UnifiSwitchMac) && !MacAddressRegex.IsMatch(req.UnifiSwitchMac))
        {
            AddError(nameof(req.UnifiSwitchMac), "UniFi Switch MAC must be a valid MAC address.");
        }

        if (req.UnifiSwitchPort.HasValue && (req.UnifiSwitchPort.Value <= 0 || req.UnifiSwitchPort.Value > 65535))
        {
            AddError(nameof(req.UnifiSwitchPort), "UniFi Switch Port must be between 1 and 65535.");
        }

        return errors.ToDictionary(k => k.Key, v => v.Value.ToArray());
    }
}
