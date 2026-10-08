import datetime as dt
import json
from pathlib import Path
import tempfile
import unittest
from reserve_ai import reserve, CAP

NOW=dt.datetime(2026,10,8,12,tzinfo=dt.timezone.utc)
class ReservationTests(unittest.TestCase):
    def test_retry_is_denied_and_next_utc_day_is_available(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'ledger.json';p.write_text('{"version":1,"days":{}}')
            self.assertTrue(reserve(p,'run-1',NOW))
            before=p.read_bytes()
            self.assertFalse(reserve(p,'run-2',NOW))
            self.assertEqual(p.read_bytes(),before)
            self.assertEqual(json.loads(before)['days']['2026-10-08']['reserved_tokens'],CAP)
            self.assertTrue(reserve(p,'tomorrow',NOW+dt.timedelta(days=1)))
            with self.assertRaises(ValueError):reserve(p,'old-clock',NOW)

    def test_corrupt_ledger_and_reset_boundary_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'ledger.json';p.write_text('{"version":1,"days":{}}')
            self.assertFalse(reserve(p,'late',NOW.replace(hour=23,minute=55)))
            p.write_text('{}')
            with self.assertRaises(ValueError):reserve(p,'invalid',NOW)
            p.write_text('invalid json')
            with self.assertRaises(ValueError):reserve(p,'invalid',NOW)

if __name__=='__main__':unittest.main()
