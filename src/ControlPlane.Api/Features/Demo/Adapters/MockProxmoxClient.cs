using ControlPlane.Api.Features.Adapters.Proxmox;

namespace ControlPlane.Api.Features.Demo.Adapters;

public class MockProxmoxClient : IProxmoxClient
{
    private readonly List<ProxmoxSnapshotItem> _snapshots = new()
    {
        new ProxmoxSnapshotItem("clean-baseline", DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds(), "Pre-upgrade snapshot baseline", 1, null)
    };

    public Task<string> CreateVmSnapshotAsync(string node, int vmid, string snapName, string? description = null, bool isLxc = false, CancellationToken ct = default)
    {
        _snapshots.Add(new ProxmoxSnapshotItem(snapName, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), description ?? "Demo snapshot", 1, null));
        return Task.FromResult($"UPID:{node}:00001234:00ABCDEF:66000000:qmsnapshot:{vmid}:root@pam:");
    }

    public Task<string> RollbackVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default)
    {
        return Task.FromResult($"UPID:{node}:00001235:00ABCDEF:66000000:qmrollback:{vmid}:root@pam:");
    }

    public Task<string> DeleteVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default)
    {
        _snapshots.RemoveAll(s => s.Name == snapName);
        return Task.FromResult($"UPID:{node}:00001236:00ABCDEF:66000000:qmdelsnapshot:{vmid}:root@pam:");
    }

    public Task<ProxmoxTaskStatus> GetTaskStatusAsync(string node, string upid, CancellationToken ct = default)
    {
        return Task.FromResult(new ProxmoxTaskStatus("stopped", "OK", "100", node, "qmsnapshot", "root@pam", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    public Task<ProxmoxTaskStatus> PollTaskCompletionAsync(string node, string upid, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        return Task.FromResult(new ProxmoxTaskStatus("stopped", "OK", "100", node, "qmsnapshot", "root@pam", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    public Task<List<ProxmoxClusterResourceDto>> DiscoverClusterResourcesAsync(CancellationToken ct = default)
    {
        var resources = new List<ProxmoxClusterResourceDto>
        {
            new("node/proxmox", "proxmox", "node", null, "proxmox", "online", 68719476736, 34359738368, 2000000000000, 850000000000, 1209600, null),
            new("qemu/100", "proxmox", "qemu", 100, "k8s-cp-01", "running", 8589934592, 4294967296, 64424509440, 21474836480, 864000, "kubernetes;control-plane"),
            new("qemu/101", "proxmox", "qemu", 101, "k8s-worker-01", "running", 17179869184, 8589934592, 85899345920, 32212254720, 864000, "kubernetes;worker"),
            new("qemu/102", "proxmox", "qemu", 102, "gitlab-runner-vm", "running", 8589934592, 3221225472, 42949672960, 15032385536, 432000, "ci;runner"),
            new("lxc/201", "proxmox", "lxc", 201, "pihole-dns", "running", 1073741824, 536870912, 10737418240, 3221225472, 1800000, "dns;adblock")
        };
        return Task.FromResult(resources);
    }

    public Task<string?> TryGetGuestIpAddressAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default)
    {
        string? ip = vmid switch
        {
            100 => "192.168.1.10",
            101 => "192.168.1.11",
            102 => "192.168.1.200",
            201 => "192.168.1.201",
            _ => null
        };
        return Task.FromResult(ip);
    }

    public Task<List<string>> TryGetGuestIpAddressesAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default)
    {
        var primary = TryGetGuestIpAddressAsync(node, vmid, isLxc, ct).Result;
        return Task.FromResult(primary != null ? new List<string> { primary } : new List<string>());
    }

    public Task<List<ProxmoxSnapshotItem>> ListVmSnapshotsAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default)
    {
        return Task.FromResult(new List<ProxmoxSnapshotItem>(_snapshots));
    }

    public Task<List<ProxmoxNodeDto>> ListNodesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<ProxmoxNodeDto>
        {
            new("proxmox", "online", 12.5, 32, 34359738368, 68719476736, 1209600)
        });
    }

    public Task<bool> HasVmAuditPermissionAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> HasSnapshotFeatureAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult(true);

    public Task<string?> TryGetGuestOsTypeAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult<string?>("l26");

    public Task<List<ProxmoxDiskItem>> GetNodeDisksAsync(string node, CancellationToken ct = default)
    {
        return Task.FromResult(new List<ProxmoxDiskItem>
        {
            new("/dev/nvme0n1", 1000204886016, "nvme", "Samsung SSD 980 PRO 1TB", "S5GXNF0R123456", "PASSED", 2),
            new("/dev/sda", 2000398934016, "sata", "Crucial MX500 2TB", "2140E5E01234", "PASSED", 5),
            new("/dev/sdb", 2000398934016, "sata", "Crucial MX500 2TB", "2140E5E01235", "PASSED", 4)
        });
    }

    public Task<List<ProxmoxZfsPoolItem>> GetNodeZfsPoolsAsync(string node, CancellationToken ct = default)
    {
        return Task.FromResult(new List<ProxmoxZfsPoolItem>
        {
            new("rpool", 950000000000, 320000000000, 630000000000, "ONLINE", 12),
            new("tank", 1900000000000, 850000000000, 1050000000000, "ONLINE", 8)
        });
    }
}
