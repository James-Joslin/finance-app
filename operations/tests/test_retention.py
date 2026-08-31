import unittest
from datetime import datetime, timedelta, timezone

from finova_backup import should_delete_blob


class RetentionSelectionTests(unittest.TestCase):
    def setUp(self):
        self.cutoff = datetime(2026, 8, 17, 2, 0, tzinfo=timezone.utc)

    def test_deletes_matching_blob_older_than_cutoff(self):
        self.assertTrue(
            should_delete_blob(
                "finances_db/2026/08/16/old.dump",
                self.cutoff - timedelta(seconds=1),
                "finances_db",
                self.cutoff,
            )
        )

    def test_retains_blob_at_cutoff_boundary(self):
        self.assertFalse(
            should_delete_blob(
                "finances_db/2026/08/17/boundary.dump",
                self.cutoff,
                "finances_db",
                self.cutoff,
            )
        )

    def test_retains_newer_blob(self):
        self.assertFalse(
            should_delete_blob(
                "finances_db/2026/08/18/new.dump",
                self.cutoff + timedelta(days=1),
                "finances_db",
                self.cutoff,
            )
        )

    def test_ignores_other_database_prefix(self):
        self.assertFalse(
            should_delete_blob(
                "other_db/2026/08/01/old.dump",
                self.cutoff - timedelta(days=30),
                "finances_db",
                self.cutoff,
            )
        )


if __name__ == "__main__":
    unittest.main()
