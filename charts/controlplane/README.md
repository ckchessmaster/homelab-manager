# ControlPlane Helm Chart

Production-ready Kubernetes Helm chart for deploying the **ControlPlane** homelab orchestration and management platform.

Published as an OCI artifact to GitHub Container Registry: `oci://ghcr.io/ckchessmaster/charts/controlplane`

---

## Quickstart

```bash
# 1. Create the target namespace
kubectl create namespace controlplane --dry-run=client -o yaml | kubectl apply -f -

# 2. Deploy using the local chart
helm upgrade --install controlplane ./charts/controlplane \
  --namespace controlplane \
  --values values.yaml

# Or deploy directly from the GHCR OCI registry
helm upgrade --install controlplane oci://ghcr.io/ckchessmaster/charts/controlplane \
  --namespace controlplane \
  --values values.yaml
```

---

## Configuration Reference

### Network Policies & Cilium Integration

ControlPlane communicates with many heterogeneous infrastructure components:
- In-cluster dependencies: PostgreSQL, Temporal, Zitadel OIDC
- Kubernetes API Server: Node drain, workload introspection, pod eviction
- Out-of-cluster homelab appliances: Proxmox VE hypervisors, Ubiquiti UniFi switches, OPNsense firewalls, Dell iDRAC BMCs
- Compute nodes: SSH bootstrapping, outbound agent WebSockets

```yaml
networkPolicy:
  enabled: true
  # Subnets permitted for outbound homelab communication (used by standard NetworkPolicy ipBlock)
  allowedEgressSubnets:
    - "192.168.0.0/16"
    - "10.0.0.0/8"

  cilium:
    enabled: false
    # Whitelisted Cilium destination entities
    toEntities:
      - kube-apiserver
    # Allow reaching cluster worker nodes ('remote-node' and 'host')
    allowClusterNodes: false
    # Allow all outbound egress ('all')
    allowAllEgress: false
    # Optional custom egress rules
    extraEgress: []
```

#### Important: Cilium Network Policies & Node Adoption

When running on clusters with **Cilium CNI** and enabling `networkPolicy.cilium.enabled: true`:

1. **Default-Deny Egress Model:**
   As soon as a `CiliumNetworkPolicy` selects the `controlplane-api` pod, Cilium enforces default-deny for all outbound traffic not explicitly permitted by an egress rule.

2. **Cluster Node Identity Discrepancy:**
   In Cilium's identity model, cluster nodes are assigned specific identities:
   - Control-plane / master nodes running the API server match `toEntities: [kube-apiserver]`.
   - Worker nodes in the cluster match `toEntities: [remote-node]`.
   - The node currently hosting the `controlplane-api` pod matches `toEntities: [host]`.
   - `toCIDR` rules in Cilium **do not match cluster nodes** (Cilium explicitly reserves `remote-node` and `host` for cluster node communication).

3. **In-Cluster Node Adoption (SSH):**
   If ControlPlane runs inside the same Kubernetes cluster whose nodes it manages or adopts via SSH:
   - Setting only `toEntities: [kube-apiserver]` allows SSH to control-plane nodes, but **silently drops** outbound TCP SYN packets to worker nodes, causing SSH connection timeouts.
   - **Recommended for homelabs managing cluster nodes:** Set `networkPolicy.cilium.allowClusterNodes: true` (which appends `remote-node` and `host` to `toEntities`).
   - If you prefer total egress freedom, set `networkPolicy.cilium.allowAllEgress: true` (or leave `networkPolicy.cilium.enabled: false` and rely on standard `NetworkPolicy`).
   - If your cluster has strict egress boundaries, leave `allowClusterNodes: false` and provide explicit rules via `extraEgress`.

---

## Values Summary

| Parameter | Description | Default |
| :--- | :--- | :--- |
| `replicaCount` | API and Frontend pod replicas | `1` |
| `image.api.repository` | API container image repository | `ghcr.io/ckchessmaster/controlplane-api` |
| `image.frontend.repository` | Frontend container image repository | `ghcr.io/ckchessmaster/controlplane-frontend` |
| `ingress.enabled` | Expose ControlPlane UI and API via Ingress | `true` |
| `temporal.enabled` | Connect to an external Temporal cluster | `false` |
| `database.host` | PostgreSQL database host | `controlplane-db` |
| `networkPolicy.enabled` | Create standard Kubernetes NetworkPolicies | `true` |
| `networkPolicy.allowedEgressSubnets` | Permitted CIDR blocks for homelab devices | `["192.168.0.0/16", "10.0.0.0/8"]` |
| `networkPolicy.cilium.enabled` | Generate CiliumNetworkPolicy resource | `false` |
| `networkPolicy.cilium.allowClusterNodes` | Permit egress to cluster worker/host nodes (`remote-node`, `host`) | `false` |
| `networkPolicy.cilium.allowAllEgress` | Permit all outbound egress (`all`) | `false` |
| `networkPolicy.cilium.toEntities` | List of allowed Cilium entities | `["kube-apiserver"]` |
