//go:build !windows

package service

import "context"

func IsWindowsService() bool {
	return false
}

func RunAsService(serviceName string, startFunc func(context.Context) error) error {
	return nil
}
