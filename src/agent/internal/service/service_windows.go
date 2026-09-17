//go:build windows

package service

import (
	"context"
	"log"
	"time"

	"golang.org/x/sys/windows/svc"
)

type agentService struct {
	startFunc func(context.Context) error
}

func (s *agentService) Execute(args []string, r <-chan svc.ChangeRequest, changes chan<- svc.Status) (ssec bool, errno uint32) {
	const cmdsAccepted = svc.AcceptStop | svc.AcceptShutdown
	changes <- svc.Status{State: svc.StartPending}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	errChan := make(chan error, 1)
	go func() {
		errChan <- s.startFunc(ctx)
	}()

	changes <- svc.Status{State: svc.Running, Accepts: cmdsAccepted}
	log.Printf("[Service] ControlPlaneAgent Windows service marked as Running.")

	for {
		select {
		case err := <-errChan:
			if err != nil {
				log.Printf("[Service] Agent terminated with error: %v", err)
			}
			changes <- svc.Status{State: svc.StopPending}
			return false, 0

		case c := <-r:
			switch c.Cmd {
			case svc.Interrogate:
				changes <- c.CurrentStatus
			case svc.Stop, svc.Shutdown:
				log.Printf("[Service] Received stop/shutdown command from Service Control Manager.")
				changes <- svc.Status{State: svc.StopPending}
				cancel()
				select {
				case <-errChan:
				case <-time.After(5 * time.Second):
				}
				changes <- svc.Status{State: svc.Stopped}
				return false, 0
			default:
				log.Printf("[Service] Unexpected control request #%d", c)
			}
		}
	}
}

func IsWindowsService() bool {
	isService, err := svc.IsWindowsService()
	if err != nil {
		return false
	}
	return isService
}

func RunAsService(serviceName string, startFunc func(context.Context) error) error {
	log.Printf("[Service] Starting %s as a Windows Service...", serviceName)
	return svc.Run(serviceName, &agentService{startFunc: startFunc})
}
