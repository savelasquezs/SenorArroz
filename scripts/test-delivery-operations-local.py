import concurrent.futures
import datetime as dt
import json
import os
import time
import urllib.error
import urllib.request
import uuid

BASE = 'http://127.0.0.1:5000/api'
HEADERS = {'Content-Type': 'application/json', 'X-Delivery-App-Version': '1.2.5',
           'X-Delivery-App-Build': '999', 'X-Delivery-App-Package': 'com.senorarroz.delivery_app'}


def call(path, data=None, expected=200):
    request = urllib.request.Request(BASE + path, headers=HEADERS,
        data=None if data is None else json.dumps(data).encode())
    start = time.perf_counter()
    try:
        response = urllib.request.urlopen(request, timeout=60)
        status, result = response.status, json.load(response)
    except urllib.error.HTTPError as error:
        status, result = error.code, json.load(error)
    assert status == expected, (path, status, result.get('code'), result.get('message'))
    print(path, status, round((time.perf_counter() - start) * 1000), 'ms', flush=True)
    return result.get('data', result) if isinstance(result, dict) else result


def intention(kind, context, session, **fields):
    return dict(operationId=str(uuid.uuid4()), kind=kind,
        recordedAt=dt.datetime.now(dt.timezone.utc).isoformat().replace('+00:00', 'Z'),
        routeId=context['routeId'], workSessionId=session['id'], **fields)


def take(ids, context, session):
    return intention('take', context, session, expectedLastTakeId=context['lastTakeId'],
                     selection={'orderIds': ids})


def main():
    login = call('/auth/login', dict(email='domiciliario@pruebas.local',
        password=os.environ['DELIVERY_LOCAL_TEST_PASSWORD'], deviceInstallationId='delivery-restructure-cli'))
    HEADERS['Authorization'] = 'Bearer ' + login['token']
    session = call('/deliverymen/me/work-session', dict(deviceInstallationId='delivery-restructure-cli',
        devicePlatform='android', deviceDescription='Local acceptance', appVersion='1.2.5+999'))
    context = call('/deliverymen/me/active-route')
    available = call('/orders/delivery/available')
    if isinstance(available, dict):
        available = available['items']
    ids = [o['id'] for o in available if (o.get('notes') or '').startswith('Prueba local de rutas #')]
    assert len(ids) >= 6, 'Reset only the dedicated local fixture orders before running this acceptance.'
    a = take(ids[:2], context, session)
    accepted = call('/deliverymen/me/operations', a)
    context = accepted['context']
    assert {o['id'] for o in context['orders']} == set(ids[:2])
    assert call('/deliverymen/me/operations', a)['replayed']
    call('/deliverymen/me/operations', take([ids[2], ids[2]], context, session), expected=400)
    assert call('/deliverymen/me/active-route')['routeId'] == context['routeId']
    order = context['orders'][0]
    correction = intention('address', context, session, orderId=order['id'],
        expectedVersion=order['managementVersion'], addressVersion=order['addressVersion'], changes={'additionalInfo': 'Prueba offline ' + str(uuid.uuid4())})
    updated = call('/deliverymen/me/operations', correction)['order']
    assert updated['addressVersion'] != order['addressVersion']
    assert (updated['total'], updated['deliveryFee']) == (order['total'], order['deliveryFee'])
    assert call('/deliverymen/me/operations', correction)['replayed']
    delivery = intention('deliver', context, session, orderId=order['id'], expectedVersion=order['managementVersion'])
    call('/deliverymen/me/operations', delivery)
    b = call('/deliverymen/me/operations', take(ids[2:4], context, session))['context']
    assert b['routeId'] != context['routeId']
    assert call('/deliverymen/me/operations', a)['context']['routeId'] == b['routeId']
    requests = [take([order_id], b, session) for order_id in ids[4:6]]

    def race(request):
        try:
            return call('/deliverymen/me/operations', request), 200
        except AssertionError as error:
            assert error.args[0][1] == 409, error
            return None, 409

    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
        results = list(executor.map(race, requests))
    assert sorted(status for _, status in results) == [200, 409]
    winner = next(result['context'] for result, status in results if status == 200)
    assert call('/deliverymen/me/active-route')['routeId'] == winner['routeId']
    assert len(winner['orders']) == 1 and winner['orders'][0]['status'].lower().replace('_', '') == 'ontheway'
    print('PASS: atomic replacement, invalid selection rollback, historical replay, address version, delivery and concurrent takes.')


if __name__ == '__main__':
    main()
