"""Local activation configuration checks; synthetic credentials, no Docker/network."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('google_config', Path(__file__).resolve().parents[1] / 'scripts/connect-google-preview.py')
config = importlib.util.module_from_spec(spec)
spec.loader.exec_module(config)


class GoogleConfigurationTests(unittest.TestCase):
    def read(self, payload):
        with tempfile.TemporaryDirectory() as folder:
            file = Path(folder) / 'client.json'
            file.write_text(json.dumps(payload))
            return config.read_credentials(file)

    def valid(self):
        return {'web': {'client_id': 'test.apps.googleusercontent.com', 'client_secret': 'synthetic-secret',
            'redirect_uris': [config.REDIRECT]}}

    def test_valid_web_client_produces_only_google_settings(self):
        values = self.read(self.valid())
        self.assertEqual(set(values), {config.PREFIX + part for part in ['Enabled', 'ClientId', 'ClientSecret']})
        self.assertEqual(values[config.PREFIX + 'Enabled'], 'true')

    def test_missing_secret_wrong_client_type_or_callback_is_rejected(self):
        for key, value in [('client_secret', ''), ('client_id', 'wrong'), ('redirect_uris', ['http://localhost:5097/'])]:
            with self.subTest(key=key):
                payload = self.valid()
                payload['web'][key] = value
                with self.assertRaises(ValueError):
                    self.read(payload)
        with self.assertRaises(ValueError):
            self.read({'installed': self.valid()['web']})

    def test_download_cannot_inject_environment_settings(self):
        for character in ['\n', '\r', '\0']:
            payload = self.valid()
            payload['web']['client_secret'] += character + 'Billing__Enabled=false'
            with self.assertRaises(ValueError):
                self.read(payload)

    def test_existing_database_billing_and_key_configuration_are_preserved(self):
        original = ['ConnectionStrings__DefaultConnection=example=db;Password=a=b', 'Billing__Enabled=true',
            'Billing__SecretKey=synthetic-stripe-key', 'Authentication__KeyRingPath=/keys', 'UNRELATED=keep']
        self.assertEqual(config.release.runtime_environment(original, {}), original)
        updated = config.release.runtime_environment(original, self.read(self.valid()))
        self.assertEqual(updated[:len(original)], original)
        with self.assertRaises(AssertionError):
            config.release.runtime_environment(original, {'KEY': 'a\nUNRELATED=overwrite'})


if __name__ == '__main__':
    unittest.main()
