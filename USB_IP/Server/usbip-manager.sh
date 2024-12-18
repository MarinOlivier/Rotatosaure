#!/bin/bash

LOG_TAG="usbip-manager"
BOUND_DEVICES_FILE="/tmp/usbip-bound-devices.txt"

log_message() {
    local message=$1
    logger -t $LOG_TAG "$message"
}

ensure_bound_devices_file() {
    if [ ! -f $BOUND_DEVICES_FILE ]; then
        touch $BOUND_DEVICES_FILE
    fi
}

bind_usb() {
    local busid=$1
    log_message "Binding USB device: $busid"
    usbip bind -b $busid
    if [ $? -eq 0 ]; then
        log_message "Successfully bound USB device: $busid"
        echo $busid >> $BOUND_DEVICES_FILE
    else
        log_message "Failed to bind USB device: $busid"
    fi
}

unbind_usb() {
    local busid=$1
    log_message "Unbinding USB device: $busid"
    usbip unbind -b $busid
    if [ $? -eq 0 ]; then
        log_message "Successfully unbound USB device: $busid"
        sed -i "/^$busid$/d" $BOUND_DEVICES_FILE
    else
        log_message "Failed to unbind USB device: $busid"
    fi
}

start_all() {
    for busid in $(usbip list -l | grep 'busid' | awk '{print $3}'); do
        bind_usb $busid
    done
}

stop_all() {
    for busid in $(cat $BOUND_DEVICES_FILE); do
        unbind_usb $busid
    done
}

case "$1" in
    start)
        log_message "Starting USBIP manager"
        ensure_bound_devices_file
        start_all
        ;;
    stop)
        log_message "Stopping USBIP manager"
        ensure_bound_devices_file
        stop_all
        rm -f $BOUND_DEVICES_FILE
        ;;
    check)
        log_message "Checking USB devices"
        ensure_bound_devices_file
        for busid in $(usbip list -l | grep 'busid' | awk '{print $3}'); do
            if ! grep -q "^$busid$" $BOUND_DEVICES_FILE; then
                bind_usb $busid
            fi
        done

        for busid in $(cat $BOUND_DEVICES_FILE); do
            if ! usbip list -l | grep -q $busid; then
                unbind_usb $busid
            fi
        done
        ;;
    *)
        echo "Usage: $0 {start|stop|check}"
        exit 1
        ;;
esac