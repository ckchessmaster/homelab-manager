//go:build windows

package metrics

import (
	"fmt"
	"sync"
	"time"
	"unsafe"

	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/registry"
)

var (
	modkernel32               = windows.NewLazySystemDLL("kernel32.dll")
	procGetSystemTimes        = modkernel32.NewProc("GetSystemTimes")
	procGlobalMemoryStatusEx  = modkernel32.NewProc("GlobalMemoryStatusEx")
)

type memoryStatusEx struct {
	cbSize                  uint32
	dwMemoryLoad            uint32
	ullTotalPhys            uint64
	ullAvailPhys            uint64
	ullTotalPageFile        uint64
	ullAvailPageFile        uint64
	ullTotalVirtual         uint64
	ullAvailVirtual         uint64
	ullAvailExtendedVirtual uint64
}

type filetime struct {
	dwLowDateTime  uint32
	dwHighDateTime uint32
}

func fileTimeToUint64(ft filetime) uint64 {
	return uint64(ft.dwHighDateTime)<<32 | uint64(ft.dwLowDateTime)
}

type windowsCollector struct {
	mu          sync.Mutex
	prevIdle    uint64
	prevKernel  uint64
	prevUser    uint64
	hasPrevStat bool
}

func newDefaultCollector() Collector {
	return newWindowsCollector()
}

func newWindowsCollector() Collector {
	return &windowsCollector{}
}

func (c *windowsCollector) Collect() (*Metrics, error) {
	cpuPct := c.getCPUUsage()
	memPct := c.getMemoryUsage()
	diskPct := c.getDiskFree()

	return &Metrics{
		CPUUsagePct:    round(cpuPct, 1),
		MemoryUsagePct: round(memPct, 1),
		DiskFreePct:    round(diskPct, 1),
	}, nil
}

func getSystemTimes(idle, kernel, user *filetime) error {
	r1, _, err := procGetSystemTimes.Call(
		uintptr(unsafe.Pointer(idle)),
		uintptr(unsafe.Pointer(kernel)),
		uintptr(unsafe.Pointer(user)),
	)
	if r1 == 0 {
		return err
	}
	return nil
}

func (c *windowsCollector) getCPUUsage() float64 {
	c.mu.Lock()
	defer c.mu.Unlock()

	var idle, kernel, user filetime
	if err := getSystemTimes(&idle, &kernel, &user); err != nil {
		return 0.0
	}

	idleU := fileTimeToUint64(idle)
	kernelU := fileTimeToUint64(kernel)
	userU := fileTimeToUint64(user)

	if !c.hasPrevStat {
		c.prevIdle = idleU
		c.prevKernel = kernelU
		c.prevUser = userU
		c.hasPrevStat = true
		time.Sleep(100 * time.Millisecond)

		if err := getSystemTimes(&idle, &kernel, &user); err != nil {
			return 0.0
		}
		idleU = fileTimeToUint64(idle)
		kernelU = fileTimeToUint64(kernel)
		userU = fileTimeToUint64(user)
	}

	dIdle := idleU - c.prevIdle
	dKernel := kernelU - c.prevKernel
	dUser := userU - c.prevUser

	c.prevIdle = idleU
	c.prevKernel = kernelU
	c.prevUser = userU

	// On Windows, kernel time includes idle time
	dTotal := dKernel + dUser
	if dTotal == 0 {
		return 0.0
	}

	if dIdle > dTotal {
		dIdle = dTotal
	}

	usage := (1.0 - (float64(dIdle) / float64(dTotal))) * 100.0
	if usage < 0.0 {
		usage = 0.0
	} else if usage > 100.0 {
		usage = 100.0
	}
	return usage
}

func (c *windowsCollector) getMemoryUsage() float64 {
	var memStatus memoryStatusEx
	memStatus.cbSize = uint32(unsafe.Sizeof(memStatus))
	r1, _, _ := procGlobalMemoryStatusEx.Call(uintptr(unsafe.Pointer(&memStatus)))
	if r1 == 0 {
		return 0.0
	}
	return float64(memStatus.dwMemoryLoad)
}

func (c *windowsCollector) getDiskFree() float64 {
	var freeBytesAvailable, totalNumberOfBytes, totalNumberOfFreeBytes uint64
	rootPath := windows.StringToUTF16Ptr("C:\\")
	if err := windows.GetDiskFreeSpaceEx(rootPath, &freeBytesAvailable, &totalNumberOfBytes, &totalNumberOfFreeBytes); err != nil {
		return 50.0
	}

	if totalNumberOfBytes == 0 {
		return 0.0
	}

	return (float64(totalNumberOfFreeBytes) / float64(totalNumberOfBytes)) * 100.0
}

func (c *windowsCollector) IsRebootRequired() bool {
	// 1. CBS RebootPending
	if k, err := registry.OpenKey(registry.LOCAL_MACHINE, `SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending`, registry.QUERY_VALUE); err == nil {
		k.Close()
		return true
	}

	// 2. WindowsUpdate RebootRequired
	if k, err := registry.OpenKey(registry.LOCAL_MACHINE, `SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired`, registry.QUERY_VALUE); err == nil {
		k.Close()
		return true
	}

	// 3. PendingFileRenameOperations in Session Manager
	if k, err := registry.OpenKey(registry.LOCAL_MACHINE, `SYSTEM\CurrentControlSet\Control\Session Manager`, registry.QUERY_VALUE); err == nil {
		defer k.Close()
		if vals, _, err := k.GetStringsValue("PendingFileRenameOperations"); err == nil && len(vals) > 0 {
			return true
		}
	}

	return false
}

func (c *windowsCollector) KernelVersion() string {
	k, err := registry.OpenKey(registry.LOCAL_MACHINE, `SOFTWARE\Microsoft\Windows NT\CurrentVersion`, registry.QUERY_VALUE)
	if err != nil {
		return "windows"
	}
	defer k.Close()

	productName, _, _ := k.GetStringValue("ProductName")
	build, _, _ := k.GetStringValue("CurrentBuild")
	displayVersion, _, _ := k.GetStringValue("DisplayVersion")

	if productName != "" {
		if displayVersion != "" && build != "" {
			return fmt.Sprintf("%s %s (Build %s)", productName, displayVersion, build)
		} else if build != "" {
			return fmt.Sprintf("%s (Build %s)", productName, build)
		}
		return productName
	}
	return "windows"
}
