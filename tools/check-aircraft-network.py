import argparse
import re
from pathlib import Path

HOST_CHECKS = {
    'client-f-pilot-no-follow', 'client-f-passenger-no-follow',
    'starter-family','fresh-aircraft-world','host-fixtures-created','client-pilot-ack',
    'native-manual-motion','native-manual-ground-taxi','client-disembark-ack',
    'native-child-pilot','reserved-child-hold-rejected','client-passenger-ack','client-crash-order','child-pilot-autopilot',
    'wreck-replicated','crash-target-reached','passengers-ejected','ejection-distinct-directions',
    'ejected-child-damage-callback','ejected-child-native-damage-blocked',
    'ejected-child-restored-reservation',
    'seated-player-native-command-rejected','seated-player-native-hold-rejected',
    'destination-flight-started','destination-flight-landed',
    'explosion-rendered-once','passengers-landed','explosion-stays-once',
    'repair-fixture-ready','client-repair-ack','repair-materials-consumed',
    'host-session-survives','malicious-client-disconnected','foreign-child-state-unchanged',
    'host-survives-rejection','scenario-complete',
}
CLIENT_CHECKS = {
    'client-f-pilot-no-outbound-follow', 'client-f-passenger-no-outbound-follow',
    'client-f-pilot', 'client-f-passenger',
    'replicated-fixtures','client-pilot-ack','client-avatar-seat','native-manual-motion',
    'native-manual-ground-taxi','manual-brake-stops','client-disembark-ack',
    'native-child-pilot','reserved-child-seat-preserved','reserved-child-hold-session-survives','foreign-child-sender-disconnected',
    'client-passenger-ack','client-crash-order','wreck-replicated','crash-target-reached','passengers-ejected',
    'destination-flight-started','destination-flight-airborne','destination-flight-landed',
    'seated-player-native-action-session-survives',
    'ejection-distinct-directions','explosion-rendered-once','passengers-landed',
    'explosion-stays-once','native-walk-to-repair','client-repair-ack',
    'client-session-survives','scenario-complete',
}
EXTENDED_HOST_CHECKS = {
    'client-f-pilot-no-follow', 'client-f-passenger-no-follow',
    'crash-target-reached',
    'reserved-child-hold-rejected', 'ejected-child-damage-callback',
    'ejected-child-native-damage-blocked', 'ejected-child-restored-reservation',
    'seated-player-native-command-rejected', 'seated-player-native-hold-rejected',
    'destination-flight-started', 'destination-flight-landed',
}
EXTENDED_CLIENT_CHECKS = {
    'client-f-pilot-no-outbound-follow', 'client-f-passenger-no-outbound-follow',
    'client-f-pilot', 'client-f-passenger',
    'crash-target-reached',
    'reserved-child-seat-preserved', 'reserved-child-hold-session-survives',
    'destination-flight-started', 'destination-flight-airborne',
    'destination-flight-landed', 'seated-player-native-action-session-survives',
}
IMPACT_HOST_CHECKS = {
    'impact-building-fixture-ready','impact-building-removed-on-host','impact-neighbor-retained-on-host',
}
IMPACT_CLIENT_CHECKS = {
    'impact-building-replicated','impact-building-removed-on-client','impact-neighbor-retained-on-client',
}

REMOTE_CURSOR_HOST_CHECKS = {
    'starter-family', 'fresh-aircraft-world', 'host-fixtures-created', 'client-pilot-ack',
    'native-destination-runway-clear', 'native-departure-runway-clear',
    'remote-cursor-native-target-walkable', 'remote-cursor-native-child-passenger',
    'remote-cursor-airborne', 'remote-cursor-host-ray-beyond-native-range', 'remote-cursor-native-release', 'remote-cursor-native-valid',
    'remote-cursor-canonical-parachute-order', 'remote-cursor-target-matches',
    'remote-cursor-range-restored', 'remote-cursor-in-range-release', 'remote-cursor-in-range-valid',
    'remote-cursor-in-range-restored', 'scenario-complete',
}
REMOTE_CURSOR_CLIENT_CHECKS = {
    'live-session', 'replicated-fixtures', 'client-pilot-ack', 'client-f-pilot',
    'client-f-pilot-no-outbound-follow', 'client-avatar-seat',
    'client-seated-mouse-callbacks-observed', 'client-seated-mouse-originals-suppressed',
    'client-seated-mouse-no-outbound-foot', 'remote-cursor-native-child-passenger',
    'remote-cursor-airborne', 'remote-cursor-avatar-airborne',
    'remote-cursor-target-beyond-native-range', 'remote-cursor-signed-look-sent', 'remote-cursor-no-ordinary-foot-packets',
    'remote-cursor-in-range-altitude', 'remote-cursor-in-range-avatar',
    'remote-cursor-target-within-native-range', 'remote-cursor-in-range-no-ordinary-foot-packets', 'scenario-complete',
}


def inspect_log(path, role, required):
    text = path.read_text(errors='replace')
    passed = set(re.findall(r'\[AircraftNetworkTest\] '+role+r' ([\w-]+)=PASS',text))
    missing = required-passed
    failures = re.findall(r'^.*(?:\[AircraftNetworkTest\].*=FAIL|\[AircraftNetworkTest\] diagnostic exception|Aircraft multiplayer diagnostic failed|Unhandled exception|NullReferenceException|MissingMethodException).*$' ,text,re.MULTILINE)
    if missing or failures:
        raise SystemExit(f'{role}: missing={sorted(missing)}, failures={failures}')
    print(f'{role}: {len(required)}/{len(required)} required checks passed ({path})')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('host_log',type=Path)
    parser.add_argument('client_log',type=Path)
    parser.add_argument('--baseline46', action='store_true')
    parser.add_argument('--impact', action='store_true')
    parser.add_argument('--remote-cursor', action='store_true')
    arguments = parser.parse_args()
    host_checks = HOST_CHECKS - EXTENDED_HOST_CHECKS if arguments.baseline46 else HOST_CHECKS
    client_checks = CLIENT_CHECKS - EXTENDED_CLIENT_CHECKS if arguments.baseline46 else CLIENT_CHECKS
    if arguments.remote_cursor:
        if arguments.impact or arguments.baseline46:
            parser.error('--remote-cursor is a separate focused fixture')
        host_checks = REMOTE_CURSOR_HOST_CHECKS
        client_checks = REMOTE_CURSOR_CLIENT_CHECKS
    if arguments.impact:
        host_checks |= IMPACT_HOST_CHECKS
        client_checks |= IMPACT_CLIENT_CHECKS
    inspect_log(arguments.host_log,'host',host_checks)
    inspect_log(arguments.client_log,'client',client_checks)


if __name__ == '__main__':
    main()
